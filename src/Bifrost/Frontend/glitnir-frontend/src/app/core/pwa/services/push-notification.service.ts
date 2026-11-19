import {HttpClient} from '@angular/common/http';
import {Service, effect, inject, signal} from '@angular/core';
import {firstValueFrom} from 'rxjs';
import {UserRole} from '../../auth/auth.model';
import {AuthStore} from '../../auth/services/auth-store';
import {PwaService} from './pwa.service';

interface PublicKeyResponse {
  publicKey: string;
}

@Service()
export class PushNotificationService {
  private readonly http = inject(HttpClient);
  private readonly authStore = inject(AuthStore);
  private readonly pwa = inject(PwaService);

  readonly isSupported = this.pwa.isSupported && 'PushManager' in window && 'Notification' in window;

  readonly permission = signal<NotificationPermission>(
    this.isSupported ? Notification.permission : 'default',
  );
  readonly isSubscribed = signal(false);
  readonly isLoading = signal(false);

  private lastSyncedUserId: string | null = null;

  constructor() {
    if (!this.isSupported) return;

    effect(() => {
      const role = this.authStore.role();
      const userId = this.authStore.user()?.userId ?? null;
      const registration = this.pwa.registration();

      if (!role || !userId || !registration) {
        this.lastSyncedUserId = null;
        return;
      }

      if (this.lastSyncedUserId === userId) return;
      this.lastSyncedUserId = userId;

      void this.syncExistingSubscription(role, registration);
    });
  }

  async enable(): Promise<boolean> {
    const role = this.authStore.role();
    if (!this.isSupported || !role || this.isLoading()) return false;

    this.isLoading.set(true);
    try {
      const permission = await Notification.requestPermission();
      this.permission.set(permission);
      if (permission !== 'granted') return false;

      const registration = await navigator.serviceWorker.ready;
      let subscription = await registration.pushManager.getSubscription();

      if (!subscription) {
        const publicKey = await this.fetchPublicKey(role);
        subscription = await registration.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: this.base64ToBytes(publicKey),
        });
      }

      await this.sendSubscriptionToBackend(role, subscription);
      this.isSubscribed.set(true);
      return true;
    } catch {
      return false;
    } finally {
      this.isLoading.set(false);
    }
  }

  async disable(): Promise<void> {
    const role = this.authStore.role();
    if (!this.isSupported || this.isLoading()) return;

    this.isLoading.set(true);
    try {
      const registration = await navigator.serviceWorker.ready;
      const subscription = await registration.pushManager.getSubscription();
      if (!subscription) {
        this.isSubscribed.set(false);
        return;
      }

      if (role) {
        await firstValueFrom(
          this.http.request('DELETE', `${this.apiUrl(role)}/subscriptions`, {
            body: {endpoint: subscription.endpoint},
            withCredentials: true,
          }),
        );
      }

      await subscription.unsubscribe();
      this.isSubscribed.set(false);
    } finally {
      this.isLoading.set(false);
    }
  }

  private async syncExistingSubscription(role: UserRole, registration: ServiceWorkerRegistration): Promise<void> {
    try {
      this.permission.set(Notification.permission);
      const subscription = await registration.pushManager.getSubscription();
      if (!subscription) {
        this.isSubscribed.set(false);
        return;
      }

      const publicKey = await this.fetchPublicKey(role);
      if (!this.hasSameApplicationServerKey(subscription, publicKey)) {
        await subscription.unsubscribe();
        this.isSubscribed.set(false);
        return;
      }

      await this.sendSubscriptionToBackend(role, subscription);
      this.isSubscribed.set(true);
    } catch {

    }
  }

  private async fetchPublicKey(role: UserRole): Promise<string> {
    const response = await firstValueFrom(
      this.http.get<PublicKeyResponse>(`${this.apiUrl(role)}/public-key`, {
        withCredentials: true,
      }),
    );
    return response.publicKey;
  }

  private async sendSubscriptionToBackend(role: UserRole, subscription: PushSubscription): Promise<void> {
    await firstValueFrom(
      this.http.put(`${this.apiUrl(role)}/subscriptions`, subscription.toJSON(), {
        withCredentials: true,
      }),
    );
  }

  private apiUrl(role: UserRole): string {
    return role === 'teacher' ? '/api/teachers/notifications/push' : '/api/students/notifications/push';
  }

  private hasSameApplicationServerKey(subscription: PushSubscription, publicKey: string): boolean {
    const current = subscription.options.applicationServerKey;
    if (!current) return false;
    const currentBytes = new Uint8Array(current);
    const expectedBytes = this.base64ToBytes(publicKey);
    return (
      currentBytes.length === expectedBytes.length &&
      currentBytes.every((byte, index) => byte === expectedBytes[index])
    );
  }

  // helper function to convert base64url to Uint8Array found here https://github.com/web-push-libs/web-push/issues/558
  private base64ToBytes(base64Url: string): Uint8Array<ArrayBuffer> {
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, '=');
    const raw = atob(padded);
    const bytes = new Uint8Array(new ArrayBuffer(raw.length));
    for (let i = 0; i < raw.length; i++) {
      bytes[i] = raw.charCodeAt(i);
    }
    return bytes;
  }
}
