import {TestBed} from '@angular/core/testing';
import {PwaService} from './pwa.service';

describe('PwaService', () => {
  const originalServiceWorker = Object.getOwnPropertyDescriptor(navigator, 'serviceWorker');
  const originalIsSecureContext = Object.getOwnPropertyDescriptor(window, 'isSecureContext');

  afterEach(() => {
    if (originalServiceWorker) {
      Object.defineProperty(navigator, 'serviceWorker', originalServiceWorker);
    } else {
      Reflect.deleteProperty(navigator, 'serviceWorker');
    }

    if (originalIsSecureContext) {
      Object.defineProperty(window, 'isSecureContext', originalIsSecureContext);
    }
  });

  function mockServiceWorker(registerImpl: ReturnType<typeof vi.fn>): void {
    Object.defineProperty(window, 'isSecureContext', {
      configurable: true,
      value: true,
    });
    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: {
        register: registerImpl,
      },
    });
  }

  it('registers /sw.js with updateViaCache none and stores the registration', async () => {
    const fakeRegistration = {
      update: vi.fn(async () => undefined),
    } as unknown as ServiceWorkerRegistration;
    const registerSpy = vi.fn(async () => fakeRegistration);
    mockServiceWorker(registerSpy);

    const service = TestBed.inject(PwaService);
    await Promise.resolve();

    expect(service.isSupported).toBe(true);
    expect(registerSpy).toHaveBeenCalledWith('/sw.js', {
      scope: '/',
      updateViaCache: 'none',
    });
    expect(service.registration()).toBe(fakeRegistration);
  });

  it('checks for service worker updates when the document becomes visible', async () => {
    const updateSpy = vi.fn(async () => undefined);
    const fakeRegistration = {
      update: updateSpy,
    } as unknown as ServiceWorkerRegistration;
    mockServiceWorker(vi.fn(async () => fakeRegistration));

    TestBed.inject(PwaService);
    await Promise.resolve();

    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      value: 'visible',
    });
    document.dispatchEvent(new Event('visibilitychange'));
    await Promise.resolve();

    expect(updateSpy).toHaveBeenCalledTimes(1);
  });

  it('does not throw when service workers are unsupported', () => {
    Object.defineProperty(window, 'isSecureContext', {
      configurable: true,
      value: false,
    });

    const service = TestBed.inject(PwaService);

    expect(service.isSupported).toBe(false);
    expect(service.registration()).toBeNull();
  });
});
