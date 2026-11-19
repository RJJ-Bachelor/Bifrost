import {DestroyRef, inject, Service, signal} from '@angular/core';

@Service()
export class PwaService {
  private readonly destroyRef = inject(DestroyRef);

  private readonly _registration = signal<ServiceWorkerRegistration | null>(null);
  readonly registration = this._registration.asReadonly();

  readonly isSupported =
    typeof navigator !== 'undefined' &&
    'serviceWorker' in navigator &&
    typeof window !== 'undefined' &&
    window.isSecureContext;

  constructor() {
    if (!this.isSupported) {
      return;
    }

    void this.register();

    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        void this.checkForUpdate();
      }
    };

    document.addEventListener('visibilitychange', onVisibilityChange);
    this.destroyRef.onDestroy(() => {
      document.removeEventListener('visibilitychange', onVisibilityChange);
    });
  }

  async checkForUpdate(): Promise<void> {
    const current = this._registration();
    if (!current) {
      return;
    }

    try {
      await current.update();
    } catch {
      // Update checks can fail transiently when offline; retry on next resume.
    }
  }

  private async register(): Promise<void> {
    try {
      const registration = await navigator.serviceWorker.register('/sw.js', {
        scope: '/',
        updateViaCache: 'none',
      });
      this._registration.set(registration);
    } catch {
      this._registration.set(null);
    }
  }
}
