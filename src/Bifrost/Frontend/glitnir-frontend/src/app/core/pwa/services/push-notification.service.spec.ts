import {provideHttpClient} from '@angular/common/http';
import {HttpTestingController, provideHttpClientTesting} from '@angular/common/http/testing';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Subject} from 'rxjs';
import {UserRole} from '../../auth/auth.model';
import {AuthStore} from '../../auth/services/auth-store';
import {NotificationEventType, NotificationMessage} from '../../realtime/notification.model';
import {RealtimeHub} from '../../realtime/services/realtime-hub';
import {PushNotificationService} from './push-notification.service';
import {PwaService} from './pwa.service';

// Valid 65-byte uncompressed P-256 public key (starts with 0x04) in base64url.
const VAPID_KEY_A = 'B' + 'A'.repeat(86);
const VAPID_KEY_B = 'B' + 'B'.repeat(86);

function base64UrlToArrayBuffer(base64Url: string): ArrayBuffer {
  const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
  const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, '=');
  const raw = atob(padded);
  const bytes = new Uint8Array(new ArrayBuffer(raw.length));
  for (let i = 0; i < raw.length; i++) {
    bytes[i] = raw.charCodeAt(i);
  }
  return bytes.buffer;
}

describe('PushNotificationService', () => {
  const originalPushManager = Object.getOwnPropertyDescriptor(window, 'PushManager');
  const originalNotification = Object.getOwnPropertyDescriptor(window, 'Notification');
  const originalServiceWorker = Object.getOwnPropertyDescriptor(navigator, 'serviceWorker');
  const originalVisibilityState = Object.getOwnPropertyDescriptor(document, 'visibilityState');

  let httpMock: HttpTestingController;
  let roleSignal: ReturnType<typeof signal<UserRole | null>>;
  let userSignal: ReturnType<typeof signal<{ userId: string } | null>>;
  let registrationSignal: ReturnType<typeof signal<ServiceWorkerRegistration | null>>;
  let notifications$: Subject<NotificationMessage>;

  let requestPermissionMock: ReturnType<typeof vi.fn>;
  let currentPermission: NotificationPermission;

  let getSubscriptionMock: ReturnType<typeof vi.fn>;
  let subscribeMock: ReturnType<typeof vi.fn>;
  let showNotificationMock: ReturnType<typeof vi.fn>;
  let fakeRegistration: ServiceWorkerRegistration;

  function createFakeSubscription(
    endpoint = 'https://push.services.mozilla.com/wpush/v2/sub-1',
    applicationServerKey: ArrayBuffer | null = base64UrlToArrayBuffer(VAPID_KEY_A),
  ): PushSubscription & { unsubscribe: ReturnType<typeof vi.fn> } {
    const unsubscribe = vi.fn(async () => true);
    return {
      endpoint,
      expirationTime: null,
      options: {
        userVisibleOnly: true,
        applicationServerKey,
      },
      getKey: vi.fn(),
      toJSON: () => ({
        endpoint,
        keys: {p256dh: 'p256dh-key', auth: 'auth-key'},
      }),
      unsubscribe,
    } as unknown as PushSubscription & { unsubscribe: ReturnType<typeof vi.fn> };
  }

  beforeEach(() => {
    currentPermission = 'default';
    requestPermissionMock = vi.fn(async () => {
      currentPermission = 'granted';
      return 'granted' as NotificationPermission;
    });

    Object.defineProperty(window, 'PushManager', {
      configurable: true,
      value: function PushManager() {
      },
    });

    const NotificationStub = Object.assign(vi.fn(), {
      requestPermission: requestPermissionMock,
    });
    Object.defineProperty(NotificationStub, 'permission', {
      get: () => currentPermission,
    });
    Object.defineProperty(window, 'Notification', {
      configurable: true,
      value: NotificationStub,
    });

    getSubscriptionMock = vi.fn(async () => null);
    subscribeMock = vi.fn();
    showNotificationMock = vi.fn(async () => undefined);

    fakeRegistration = {
      pushManager: {
        getSubscription: getSubscriptionMock,
        subscribe: subscribeMock,
      },
      showNotification: showNotificationMock,
    } as unknown as ServiceWorkerRegistration;

    Object.defineProperty(navigator, 'serviceWorker', {
      configurable: true,
      value: {
        ready: Promise.resolve(fakeRegistration),
      },
    });

    roleSignal = signal<UserRole | null>('student');
    userSignal = signal<{ userId: string } | null>(null);
    registrationSignal = signal<ServiceWorkerRegistration | null>(fakeRegistration);
    notifications$ = new Subject<NotificationMessage>();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthStore,
          useValue: {
            role: roleSignal,
            user: userSignal,
          },
        },
        {
          provide: PwaService,
          useValue: {
            isSupported: true,
            registration: registrationSignal,
          },
        },
        {
          provide: RealtimeHub,
          useValue: {
            notifications$: notifications$.asObservable(),
          },
        },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();

    if (originalPushManager) {
      Object.defineProperty(window, 'PushManager', originalPushManager);
    } else {
      Reflect.deleteProperty(window, 'PushManager');
    }

    if (originalNotification) {
      Object.defineProperty(window, 'Notification', originalNotification);
    } else {
      Reflect.deleteProperty(window, 'Notification');
    }

    if (originalServiceWorker) {
      Object.defineProperty(navigator, 'serviceWorker', originalServiceWorker);
    } else {
      Reflect.deleteProperty(navigator, 'serviceWorker');
    }

    if (originalVisibilityState) {
      Object.defineProperty(document, 'visibilityState', originalVisibilityState);
    }
  });

  it('fetches the role public key, subscribes via PushManager, and registers with backend on enable()', async () => {
    roleSignal.set('teacher');
    const createdSub = createFakeSubscription();
    subscribeMock.mockResolvedValue(createdSub);

    const service = TestBed.inject(PushNotificationService);
    const enablePromise = service.enable();

    await vi.waitFor(() => {
      const req = httpMock.expectOne('/api/teachers/notifications/push/public-key');
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBe(true);
      req.flush({publicKey: VAPID_KEY_A});
    });

    await vi.waitFor(() => {
      const putReq = httpMock.expectOne('/api/teachers/notifications/push/subscriptions');
      expect(putReq.request.method).toBe('PUT');
      expect(putReq.request.withCredentials).toBe(true);
      expect(putReq.request.body).toEqual(createdSub.toJSON());
      putReq.flush(null);
    });

    const result = await enablePromise;
    expect(result).toBe(true);
    expect(service.isSubscribed()).toBe(true);
    expect(service.error()).toBeNull();
    expect(subscribeMock).toHaveBeenCalledWith({
      userVisibleOnly: true,
      applicationServerKey: expect.any(Uint8Array),
    });
  });

  it('returns false without calling backend when browser permission is denied', async () => {
    requestPermissionMock.mockImplementationOnce(async () => {
      currentPermission = 'denied';
      return 'denied';
    });

    const service = TestBed.inject(PushNotificationService);
    const result = await service.enable();

    expect(result).toBe(false);
    expect(service.permission()).toBe('denied');
    expect(service.isSubscribed()).toBe(false);
    httpMock.expectNone('/api/students/notifications/push/public-key');
  });

  it('sets an error message when backend subscription fails', async () => {
    const createdSub = createFakeSubscription();
    subscribeMock.mockResolvedValue(createdSub);

    const service = TestBed.inject(PushNotificationService);
    const enablePromise = service.enable();

    await vi.waitFor(() => {
      httpMock.expectOne('/api/students/notifications/push/public-key').flush({publicKey: VAPID_KEY_A});
    });

    await vi.waitFor(() => {
      httpMock
        .expectOne('/api/students/notifications/push/subscriptions')
        .flush({message: 'Bad request'}, {status: 400, statusText: 'Bad Request'});
    });

    const result = await enablePromise;
    expect(result).toBe(false);
    expect(service.isSubscribed()).toBe(false);
    expect(service.error()).toBe('Kunne ikke aktivere notifikationer. Prøv igen.');
  });

  it('deletes subscription on backend and unsubscribes locally on disable()', async () => {
    const existingSub = createFakeSubscription();
    getSubscriptionMock.mockResolvedValue(existingSub);

    const service = TestBed.inject(PushNotificationService);
    service.isSubscribed.set(true);

    const disablePromise = service.disable();

    await vi.waitFor(() => {
      const delReq = httpMock.expectOne('/api/students/notifications/push/subscriptions');
      expect(delReq.request.method).toBe('DELETE');
      expect(delReq.request.body).toEqual({endpoint: existingSub.endpoint});
      delReq.flush(null);
    });

    await disablePromise;
    expect(existingSub.unsubscribe).toHaveBeenCalledTimes(1);
    expect(service.isSubscribed()).toBe(false);
  });

  it('still unsubscribes locally on disable() when backend DELETE fails', async () => {
    const existingSub = createFakeSubscription();
    getSubscriptionMock.mockResolvedValue(existingSub);

    const service = TestBed.inject(PushNotificationService);
    service.isSubscribed.set(true);

    const disablePromise = service.disable();

    await vi.waitFor(() => {
      httpMock
        .expectOne('/api/students/notifications/push/subscriptions')
        .flush({ message: 'Server error' }, { status: 500, statusText: 'Internal Server Error' });
    });

    await disablePromise;
    expect(existingSub.unsubscribe).toHaveBeenCalledTimes(1);
    expect(service.isSubscribed()).toBe(false);
  });

  it('unsubscribes an existing browser subscription when the server VAPID public key has rotated', async () => {
    const staleSub = createFakeSubscription(
      'https://push.services.mozilla.com/wpush/v2/stale',
      base64UrlToArrayBuffer(VAPID_KEY_A),
    );
    getSubscriptionMock.mockResolvedValue(staleSub);

    const service = TestBed.inject(PushNotificationService);
    userSignal.set({userId: 'student-1'});
    TestBed.tick();

    await vi.waitFor(() => {
      httpMock.expectOne('/api/students/notifications/push/public-key').flush({publicKey: VAPID_KEY_B});
    });

    await vi.waitFor(() => {
      expect(staleSub.unsubscribe).toHaveBeenCalledTimes(1);
    });
    expect(service.isSubscribed()).toBe(false);
    httpMock.expectNone('/api/students/notifications/push/subscriptions');
  });

  it('shows a local notification on RealtimeOrPush events when subscribed and tab is in background', async () => {
    currentPermission = 'granted';
    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      value: 'hidden',
    });
    vi.spyOn(document, 'hasFocus').mockReturnValue(false);
    const service = TestBed.inject(PushNotificationService);
    service.isSubscribed.set(true);
    notifications$.next({
      notificationId: '11111111-2222-3333-4444-555555555555',
      userId: 'student-1',
      sessionId: 'session-42',
      helpRequestId: 'hr-9',
      generation: 1,
      change: 'Updated',
      status: 'Answered',
      eventType: NotificationEventType.HelpRequestAnswered,
      deliveryType: 'RealtimeOrPush',
      message: 'Dit spørgsmål er besvaret.',
      createdAt: '2026-11-19T18:00:00Z',
    });
    await vi.waitFor(() => {
      expect(showNotificationMock).toHaveBeenCalledWith('Bifrost', {
        body: 'Dit spørgsmål er besvaret.',
        tag: '11111111222233334444555555555555',
        icon: '/icons/icon-192.png',
        badge: '/icons/badge-96.png',
        data: {
          notificationId: '11111111-2222-3333-4444-555555555555',
          sessionId: 'session-42',
          helpRequestId: 'hr-9',
          generation: 1,
          change: 'Updated',
          status: 'Answered',
          eventType: NotificationEventType.HelpRequestAnswered,
        },
      });
    });
  });
  
  it('does not show a local notification when the tab is visible and focused', async () => {
    currentPermission = 'granted';
    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      value: 'visible',
    });
    vi.spyOn(document, 'hasFocus').mockReturnValue(true);
    const service = TestBed.inject(PushNotificationService);
    service.isSubscribed.set(true);
    notifications$.next({
      notificationId: '11111111-2222-3333-4444-555555555555',
      userId: 'teacher-1',
      sessionId: 'session-42',
      helpRequestId: null,
      generation: 1,
      change: 'Created',
      status: null,
      eventType: NotificationEventType.HelpRequestCreated,
      deliveryType: 'RealtimeOrPush',
      message: 'Ny anmodning i køen.',
      createdAt: '2026-11-19T18:00:00Z',
    });
    await Promise.resolve();
    expect(showNotificationMock).not.toHaveBeenCalled();
  });
});
