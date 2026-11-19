import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {Subject} from 'rxjs';
import {AuthStore} from '../../auth/services/auth-store';
import {NotificationEventType, type NotificationMessage} from '../notification.model';
import {NotificationAlertService} from './notification-alert.service';
import {RealtimeHub} from './realtime-hub';

function makeNotification(overrides: Partial<NotificationMessage> = {}): NotificationMessage {
  return {
    notificationId: 'n-1',
    userId: 'u-1',
    message: 'Det er din tur!',
    createdAt: '2026-01-01T00:00:00Z',
    sessionId: 'session-1',
    helpRequestId: 'req-1',
    generation: 1,
    change: null,
    status: null,
    eventType: NotificationEventType.HelpRequestStarted,
    deliveryType: 'RealtimeOrPush',
    ...overrides,
  };
}

describe('NotificationAlertService', () => {
  let notifications$: Subject<NotificationMessage>;
  let roleSignal: ReturnType<typeof signal<'student' | 'teacher' | null>>;
  let navigateByUrlSpy: ReturnType<typeof vi.fn>;
  let currentUrl: string;
  let service: NotificationAlertService;

  beforeEach(() => {
    notifications$ = new Subject<NotificationMessage>();
    roleSignal = signal<'student' | 'teacher' | null>('student');
    navigateByUrlSpy = vi.fn();
    currentUrl = '/sessions/session-1';

    TestBed.configureTestingModule({
      providers: [
        NotificationAlertService,
        {provide: RealtimeHub, useValue: {notifications$: notifications$.asObservable()}},
        {provide: AuthStore, useValue: {role: roleSignal.asReadonly()}},
        {
          provide: Router,
          useValue: {
            get url() {
              return currentUrl;
            },
            navigateByUrl: navigateByUrlSpy,
          },
        },
      ],
    });

    service = TestBed.inject(NotificationAlertService);
    vi.spyOn(service as unknown as { isWindowFocused: () => boolean }, 'isWindowFocused').mockReturnValue(true);
  });

  it('shows alert for RealtimeOrPush when student is not on the queue page', () => {
    const item = makeNotification();
    notifications$.next(item);

    expect(service.notification()).toEqual(item);
  });

  it('ignores alert when window is not focused', () => {
    vi.spyOn(service as unknown as { isWindowFocused: () => boolean }, 'isWindowFocused').mockReturnValue(false);

    notifications$.next(makeNotification());

    expect(service.notification()).toBeNull();
  });

  it('ignores RealtimeOnly notifications', () => {
    notifications$.next(makeNotification({deliveryType: 'RealtimeOnly'}));

    expect(service.notification()).toBeNull();
  });

  it('does not show alert when student is already on the session queue page', () => {
    currentUrl = '/sessions/session-1/queue';
    notifications$.next(makeNotification());

    expect(service.notification()).toBeNull();
  });

  it('navigates to student queue and clears alert on goToNotification', () => {
    notifications$.next(makeNotification({sessionId: 'session-1'}));
    service.goToNotification();

    expect(navigateByUrlSpy).toHaveBeenCalledWith('/sessions/session-1/queue');
    expect(service.notification()).toBeNull();
  });

  it('shows alert for teacher outside session page and navigates to session', () => {
    roleSignal.set('teacher');
    currentUrl = '/teacher';

    notifications$.next(
      makeNotification({
        sessionId: 'session-42',
        eventType: NotificationEventType.HelpRequestCreated,
        message: 'Ny hjælpeanmodning',
      }),
    );

    expect(service.notification()?.message).toBe('Ny hjælpeanmodning');

    service.goToNotification();
    expect(navigateByUrlSpy).toHaveBeenCalledWith('/teacher/sessions/session-42');
  });
});
