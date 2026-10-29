import { HttpResourceRef } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, of } from 'rxjs';
import { FaqApi } from '../../../faq';
import { HelpRequestApi } from '../../../help-request';
import { SessionApi } from '../../../session';
import { RealtimeHub } from '../../../../core/realtime/services/realtime-hub';
import {
  NotificationEventType,
  type NotificationMessage,
} from '../../../../core/realtime/notification.model';
import { TeacherSession } from './teacher-session';

function fakeResource<T>(initial: T): HttpResourceRef<T> {
  return {
    value: signal(initial).asReadonly(),
    isLoading: signal(false).asReadonly(),
    error: signal(null).asReadonly(),
    reload: vi.fn(),
  } as unknown as HttpResourceRef<T>;
}

function makeNotification(overrides: Partial<NotificationMessage> = {}): NotificationMessage {
  return {
    notificationId: 'notification-1',
    userId: 'user-1',
    message: '',
    createdAt: '2026-01-01T10:00:00Z',
    sessionId: 'session-1',
    helpRequestId: null,
    generation: null,
    change: null,
    status: null,
    eventType: NotificationEventType.SessionChanged,
    deliveryType: 'RealtimeOnly',
    ...overrides,
  };
}

interface SetupResult {
  fixture: ComponentFixture<TeacherSession>;
  notifications: Subject<NotificationMessage>;
  router: {
    navigateByUrl: ReturnType<typeof vi.fn>;
    navigate: ReturnType<typeof vi.fn>;
    createUrlTree: ReturnType<typeof vi.fn>;
    serializeUrl: ReturnType<typeof vi.fn>;
  };
  takeNext: ReturnType<typeof vi.fn>;
  reloads: {
    session: ReturnType<typeof vi.fn>;
    queue: ReturnType<typeof vi.fn>;
    statistics: ReturnType<typeof vi.fn>;
    archives: ReturnType<typeof vi.fn>;
    faqs: ReturnType<typeof vi.fn>;
  };
}

describe('TeacherSession', () => {
  function setup(queueOverride: { unreservedWaitingCount?: number } = {}): SetupResult {
    const router = {
      navigateByUrl: vi.fn(),
      navigate: vi.fn(),
      createUrlTree: vi.fn(() => ({})),
      serializeUrl: vi.fn(() => '/teacher/sessions/session-1/dashboard'),
    };

    const sessionResource = fakeResource({
      id: 'session-1',
      title: 'SWBAD',
      currentGeneration: 1,
      lastClearedAtUtc: null,
    });
    const statisticsResource = fakeResource({
      physicalCount: 0,
      digitalCount: 0,
      helpedStudentCount: 0,
      averageWaitMinutes: null,
    });
    const archivesResource = fakeResource<unknown[]>([]);
    const queueResource = fakeResource({
      waitingCount: 0,
      unreservedWaitingCount: queueOverride.unreservedWaitingCount ?? 1,
      generation: 1,
      frontQueuedAt: null,
      myReservedRequestId: null,
    });
    const faqsResource = fakeResource<unknown[]>([]);

    const sessionApi = {
      getTeacherSession: () => sessionResource,
      getTeacherStatistics: () => statisticsResource,
      getTeacherArchives: () => archivesResource,
    };
    const takeNext = vi.fn(() => of({ id: 'request-1' }));
    const helpRequestApi = {
      getTeacherQueue: () => queueResource,
      takeNext,
    };
    const faqApi = {
      getTeacherFaqs: () => faqsResource,
    };
    const notifications = new Subject<NotificationMessage>();

    TestBed.configureTestingModule({
      providers: [
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { snapshot: { params: { id: 'session-1' } } } },
        { provide: SessionApi, useValue: sessionApi },
        { provide: HelpRequestApi, useValue: helpRequestApi },
        { provide: FaqApi, useValue: faqApi },
        { provide: RealtimeHub, useValue: { notifications$: notifications.asObservable() } },
      ],
    });

    const fixture = TestBed.createComponent(TeacherSession);
    fixture.componentRef.setInput('id', 'session-1');
    fixture.detectChanges();

    return {
      fixture,
      notifications,
      router,
      takeNext,
      reloads: {
        session: sessionResource.reload as ReturnType<typeof vi.fn>,
        queue: queueResource.reload as ReturnType<typeof vi.fn>,
        statistics: statisticsResource.reload as ReturnType<typeof vi.fn>,
        archives: archivesResource.reload as ReturnType<typeof vi.fn>,
        faqs: faqsResource.reload as ReturnType<typeof vi.fn>,
      },
    };
  }

  describe('realtime events', () => {
    it.each([
      NotificationEventType.HelpRequestCreated,
      NotificationEventType.StudentMessageAdded,
      NotificationEventType.SessionChanged,
    ])('reloads the queue on %s', (eventType) => {
      const { notifications, reloads } = setup();

      notifications.next(makeNotification({ eventType }));

      expect(reloads.queue).toHaveBeenCalled();
    });

    it.each(['Answered', 'Completed', 'FaqAccepted'])(
      'reloads statistics when the session change is %s',
      (change) => {
        const { notifications, reloads } = setup();

        notifications.next(
          makeNotification({ eventType: NotificationEventType.SessionChanged, change }),
        );

        expect(reloads.statistics).toHaveBeenCalled();
      },
    );

    it('reloads faqs when the session change is FaqAnswered', () => {
      const { notifications, reloads } = setup();

      notifications.next(
        makeNotification({
          eventType: NotificationEventType.SessionChanged,
          change: 'FaqAnswered',
        }),
      );

      expect(reloads.faqs).toHaveBeenCalled();
    });

    it('reloads session, faqs and statistics when the session is cleared', () => {
      const { notifications, reloads } = setup();

      notifications.next(
        makeNotification({ eventType: NotificationEventType.SessionChanged, change: 'Cleared' }),
      );

      expect(reloads.session).toHaveBeenCalled();
      expect(reloads.faqs).toHaveBeenCalled();
      expect(reloads.statistics).toHaveBeenCalled();
    });

    it('reloads archives alongside session, faqs and statistics when archived', () => {
      const { notifications, reloads } = setup();

      notifications.next(
        makeNotification({ eventType: NotificationEventType.SessionChanged, change: 'Archived' }),
      );

      expect(reloads.session).toHaveBeenCalled();
      expect(reloads.faqs).toHaveBeenCalled();
      expect(reloads.statistics).toHaveBeenCalled();
      expect(reloads.archives).toHaveBeenCalled();
    });

    it('reloads faqs on FaqCreated', () => {
      const { notifications, reloads } = setup();

      notifications.next(makeNotification({ eventType: NotificationEventType.FaqCreated }));

      expect(reloads.faqs).toHaveBeenCalled();
    });

    it('ignores events from other sessions', () => {
      const { notifications, reloads } = setup();

      notifications.next(
        makeNotification({ eventType: NotificationEventType.SessionChanged, sessionId: 'other' }),
      );

      expect(reloads.queue).not.toHaveBeenCalled();
      expect(reloads.session).not.toHaveBeenCalled();
      expect(reloads.faqs).not.toHaveBeenCalled();
      expect(reloads.statistics).not.toHaveBeenCalled();
    });
  });

  describe('takeNext', () => {
    type ComponentAccess = { takeNext: () => void };

    it('does nothing when no requests are unreserved', () => {
      const { fixture, takeNext } = setup({ unreservedWaitingCount: 0 });
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.takeNext();

      expect(takeNext).not.toHaveBeenCalled();
    });

    it('navigates to the answer page when a request is returned', () => {
      const { fixture, takeNext, router } = setup({ unreservedWaitingCount: 1 });
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.takeNext();

      expect(takeNext).toHaveBeenCalledWith('session-1');
      expect(router.navigate).toHaveBeenCalledWith(['answer', 'request-1'], expect.anything());
    });

    it('reloads the queue when the backend returns null', () => {
      const { fixture, takeNext, router, reloads } = setup({ unreservedWaitingCount: 1 });
      takeNext.mockReturnValueOnce(of(null));
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.takeNext();

      expect(reloads.queue).toHaveBeenCalled();
      expect(router.navigate).not.toHaveBeenCalled();
    });
  });
});
