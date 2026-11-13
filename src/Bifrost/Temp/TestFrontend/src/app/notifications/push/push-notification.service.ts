import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom, timeout } from 'rxjs';

type Role = 'students' | 'teachers';
interface Identity { role: Role; userId: string; }
interface Owner extends Identity { endpoint: string; }

const ownerKey = 'bifrost-push-owner';

@Injectable({ providedIn: 'root' })
export class PushNotificationService {
  private readonly http = inject(HttpClient);
  readonly supported = signal(window.isSecureContext && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window);
  readonly hasIdentity = signal(false);
  readonly busy = signal(false);
  readonly ready = signal(false);
  readonly enabled = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly offline = signal(navigator.onLine === false);
  readonly unavailableReason = window.isSecureContext
    ? 'Denne browser understøtter ikke push. På iPhone og iPad skal appen først føjes til hjemmeskærmen fra Safari.'
    : 'Push kræver HTTPS. På denne computer kan localhost også bruges.';
  private identity?: Identity;
  private registration?: ServiceWorkerRegistration;
  private publicKey?: string;
  private revision = 0;
  private pending: Promise<void> = Promise.resolve();
  private worker: Promise<ServiceWorkerRegistration | undefined>;

  constructor() {
    this.worker = this.registerWorker();
    window.addEventListener('online', () => this.offline.set(false));
    window.addEventListener('offline', () => this.offline.set(true));
    window.addEventListener('storage', event => {
      if (event.key !== ownerKey || !this.identity) return;
      // A different tab may have changed the account owning this browser endpoint.
      this.revision++;
      this.enabled.set(false);
      this.ready.set(false);
      this.busy.set(false);
      this.notice.set('Push er ændret i en anden fane. Tryk på Prøv igen for at kontrollere abonnementet.');
    });
  }

  setIdentity(role: Role, userId: string): Promise<void> {
    const previous = this.identity;
    this.identity = { role, userId };
    this.hasIdentity.set(true);
    const revision = ++this.revision;
    this.enabled.set(false);
    this.ready.set(false);
    this.publicKey = undefined;
    this.error.set('');
    this.notice.set('');
    this.busy.set(this.supported());
    return this.enqueue(async () => {
      if (!this.supported() || !this.current(revision)) return;
      try {
        this.registration = await this.worker;
        if (!this.current(revision)) return;
        if (!this.registration) throw new Error('worker');
        let subscription = await this.registration.pushManager.getSubscription();
        if (!this.current(revision)) return;
        const owner = this.readOwner();
        if (subscription && (!this.matches(owner, this.identity) || owner?.endpoint !== subscription.endpoint)) {
          const authenticated = !!owner && (this.matches(owner, this.identity)
            || (previous?.role !== role && this.matches(owner, previous)));
          await this.remove(subscription, owner, authenticated);
          subscription = null;
        }
        if (!this.current(revision)) return;
        const result = await this.request<{ publicKey: string }>('GET', role, 'public-key');
        this.decodeKey(result.publicKey);
        if (!this.current(revision)) return;
        this.publicKey = result.publicKey;
        if (subscription && !this.sameKey(subscription, result.publicKey)) {
          await this.remove(subscription, owner, true);
          subscription = null;
          if (this.current(revision)) this.notice.set('Push-nøglen er ændret. Aktivér push igen på denne enhed.');
        }
        if (subscription) {
          await this.save(subscription, revision);
          if (this.current(revision)) this.enabled.set(true);
        }
        if (this.current(revision)) this.ready.set(true);
      } catch (failure) {
        if (this.current(revision)) this.showError(failure);
      } finally {
        if (this.current(revision)) this.busy.set(false);
      }
    });
  }

  retry(): Promise<void> {
    if (this.supported() && !this.registration) this.worker = this.registerWorker();
    return this.identity ? this.setIdentity(this.identity.role, this.identity.userId) : Promise.resolve();
  }

  enable(): Promise<void> {
    if (this.busy() || !this.ready() || !this.identity || !this.registration || !this.publicKey) return Promise.resolve();
    const revision = this.revision;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    // Keep the permission request in the button's synchronous user gesture.
    let permission: Promise<NotificationPermission>;
    try {
      permission = Notification.permission === 'default' ? Notification.requestPermission() : Promise.resolve(Notification.permission);
    } catch (failure) {
      this.showError(failure);
      this.busy.set(false);
      return Promise.resolve();
    }
    return this.enqueue(async () => {
      let created: PushSubscription | undefined;
      try {
        if (await permission !== 'granted') throw new Error('permission');
        if (!this.current(revision)) return;
        const registration = this.registration!;
        let subscription = await registration.pushManager.getSubscription();
        if (!this.current(revision)) return;
        const owner = this.readOwner();
        if (subscription && (!this.matches(owner, this.identity) || owner?.endpoint !== subscription.endpoint)) {
          await this.remove(subscription, owner, false);
          subscription = null;
        }
        if (!this.current(revision)) return;
        if (!subscription) {
          this.checkStorage();
          subscription = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: this.decodeKey(this.publicKey!) });
          created = subscription;
        }
        if (!this.current(revision)) { if (created) await this.remove(created, undefined, false); return; }
        await this.save(subscription, revision);
        if (this.current(revision)) {
          this.enabled.set(true);
          this.notice.set('Push er aktiveret på denne enhed. Beskeder sendes, når du ikke har forbindelse til SignalR.');
        }
      } catch (failure) {
        if (created) {
          try { await this.remove(created, this.readOwner(), this.current(revision)); }
          catch { /* Keep its owner record so a retry can safely clean up. */ }
        }
        if (this.current(revision)) this.showError(failure);
      } finally {
        if (this.current(revision)) this.busy.set(false);
      }
    });
  }

  disable(): Promise<void> {
    if (this.busy()) return Promise.resolve();
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    const revision = this.revision;
    return this.enqueue(async () => {
      try {
        await this.cleanup(true);
        if (this.current(revision)) {
          this.enabled.set(false);
          this.notice.set('Push er slået fra på denne enhed.');
        }
      } catch (failure) { if (this.current(revision)) this.showError(failure); }
      finally { if (this.current(revision)) this.busy.set(false); }
    });
  }

  prepareIdentityChange(): Promise<void> {
    const revision = ++this.revision;
    this.ready.set(false);
    this.enabled.set(false);
    this.busy.set(true);
    return this.enqueue(async () => {
      try { await this.cleanup(true); }
      catch (failure) {
        if (this.current(revision)) this.showError(failure);
        throw new Error('Push kunne ikke stoppes sikkert. Prøv igen, før du skifter identitet.');
      }
      finally { if (this.current(revision)) this.busy.set(false); }
    });
  }

  clearIdentity(role?: Role): Promise<void> {
    if (role && this.identity?.role !== role) return Promise.resolve();
    this.identity = undefined;
    this.hasIdentity.set(false);
    const revision = ++this.revision;
    this.ready.set(false);
    this.enabled.set(false);
    this.busy.set(false);
    return this.enqueue(async () => {
      try { await this.cleanup(false); }
      catch (failure) { if (this.current(revision)) this.showError(failure); }
    });
  }

  private enqueue(action: () => Promise<void>): Promise<void> {
    const result = this.pending.then(action, action);
    this.pending = result.catch(() => {});
    return result;
  }

  private current(revision: number) { return revision === this.revision; }
  private matches(owner: Owner | undefined, identity: Identity | undefined) {
    return !!owner && !!identity && owner.role === identity.role && owner.userId === identity.userId;
  }

  private async save(subscription: PushSubscription, revision: number) {
    if (!this.current(revision) || !this.identity) return;
    const owner = { ...this.identity, endpoint: subscription.endpoint };
    localStorage.setItem(ownerKey, JSON.stringify(owner));
    const stored = this.readOwner();
    if (!this.matches(stored, owner) || stored?.endpoint !== owner.endpoint) throw new Error('storage');
    await this.request('PUT', owner.role, 'subscriptions', subscription.toJSON());
  }

  private async cleanup(authenticated: boolean) {
    const registration = this.registration ?? await this.worker;
    const owner = this.readOwner();
    if (!registration && owner) {
      if (!authenticated || !this.matches(owner, this.identity)) throw new Error('cleanup');
      await this.request('DELETE', owner.role, 'subscriptions', { endpoint: owner.endpoint });
      this.forgetOwner(owner.endpoint);
      return;
    }
    const subscription = await registration?.pushManager.getSubscription();
    if (subscription) await this.remove(subscription, owner, authenticated && this.matches(owner, this.identity));
    else if (owner && authenticated && this.matches(owner, this.identity)) {
      try { await this.request('DELETE', owner.role, 'subscriptions', { endpoint: owner.endpoint }); } catch { /* No active browser endpoint remains. */ }
      this.forgetOwner(owner.endpoint);
    }
  }

  private async remove(subscription: PushSubscription, owner: Owner | undefined, authenticated: boolean) {
    let safe = false;
    try {
      safe = await subscription.unsubscribe();
      if (!safe) {
        const registration = this.registration ?? await this.worker;
        if (registration) safe = (await registration.pushManager.getSubscription())?.endpoint !== subscription.endpoint;
      }
    } catch { /* Authenticated removal can still stop delivery if browser removal fails. */ }
    if (authenticated && owner?.endpoint === subscription.endpoint) {
      try {
        await this.request('DELETE', owner.role, 'subscriptions', { endpoint: subscription.endpoint });
        safe = true;
      } catch { /* Expired browser endpoints are also removed by Gna on delivery. */ }
    }
    if (!safe) throw new Error('cleanup');
    this.forgetOwner(subscription.endpoint);
  }

  private readOwner(): Owner | undefined {
    try {
      const owner = JSON.parse(localStorage.getItem(ownerKey) ?? 'null') as Owner | null;
      return owner && (owner.role === 'students' || owner.role === 'teachers') && typeof owner.userId === 'string'
        && typeof owner.endpoint === 'string' ? owner : undefined;
    } catch { return undefined; }
  }

  private forgetOwner(endpoint: string) {
    try { if (this.readOwner()?.endpoint === endpoint) localStorage.removeItem(ownerKey); } catch { /* Browser removal already stopped delivery. */ }
  }

  private checkStorage() {
    try { localStorage.setItem(ownerKey + '-check', '1'); localStorage.removeItem(ownerKey + '-check'); }
    catch { throw new Error('storage'); }
  }

  private request<T = void>(method: string, role: Role, path: string, body?: unknown): Promise<T> {
    return firstValueFrom(this.http.request<T>(method, `/api/${role}/notifications/push/${path}`,
      { body, withCredentials: true }).pipe(timeout(15000)));
  }

  private decodeKey(value: string): Uint8Array<ArrayBuffer> {
    try {
      const binary = atob(value.replace(/-/g, '+').replace(/_/g, '/'));
      if (binary.length !== 65 || binary.charCodeAt(0) !== 4) throw new Error();
      return Uint8Array.from(binary, character => character.charCodeAt(0));
    } catch { throw new Error('key'); }
  }

  private sameKey(subscription: PushSubscription, key: string) {
    const current = subscription.options.applicationServerKey;
    const expected = this.decodeKey(key);
    return !!current && new Uint8Array(current).every((value, index) => value === expected[index]) && current.byteLength === expected.length;
  }

  private async waitForWorker(): Promise<ServiceWorkerRegistration> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      return await Promise.race([navigator.serviceWorker.ready, new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error('worker')), 15000);
      })]);
    } finally { clearTimeout(timer); }
  }

  private registerWorker(): Promise<ServiceWorkerRegistration | undefined> {
    return this.supported() ? navigator.serviceWorker.register('/push-sw.js', { scope: '/' })
      .then(async registration => registration.active ? registration : this.waitForWorker())
      .catch(() => undefined) : Promise.resolve(undefined);
  }

  private showError(failure: unknown) {
    const status = (failure as { status?: number })?.status;
    const message = (failure as Error)?.message;
    this.error.set(message === 'permission' ? 'Push er ikke tilladt. Tillad notifikationer i browserens eller telefonens indstillinger, og prøv igen.'
      : message === 'storage' ? 'Browseren skal tillade lokal lagring, før push kan aktiveres sikkert.'
      : message === 'cleanup' ? 'Det tidligere push-abonnement kunne ikke stoppes. Kontrollér forbindelsen, og prøv igen.'
      : message === 'worker' ? 'Appens push-modtager kunne ikke startes. Genindlæs siden, og prøv igen.'
      : message === 'key' ? 'Serverens push-nøgle er ugyldig. Kontrollér Gnas VAPID-konfiguration.'
      : status === 503 ? 'Push er ikke aktiveret på serveren. Kontrollér Gnas push-konfiguration.'
      : status === 401 || status === 403 ? 'Din identitet kunne ikke bekræftes til push. Genindlæs siden, eller log ind igen.'
      : 'Push kunne ikke opdateres. Kontrollér forbindelsen, og prøv igen.');
  }
}
