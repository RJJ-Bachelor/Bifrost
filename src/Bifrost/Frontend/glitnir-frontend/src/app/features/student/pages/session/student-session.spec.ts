import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, of } from 'rxjs';
import { FaqApi } from '../../../faq';
import {
  HelpRequestApi,
  type HelpRequest,
  type HelpRequestStatus,
  type HelpType,
} from '../../../help-request';
import { SessionApi } from '../../../session';
import { RealtimeHub } from '../../../../core/realtime/services/realtime-hub';
import {
  NotificationEventType,
  type NotificationMessage,
} from '../../../../core/realtime/notification.model';
import { fakeResource } from '../../../../../test-utils/fake-resource';
import { makeNotification } from '../../../../../test-utils/notification';
import { StudentSession } from './student-session';

function makeRequest(index: number): HelpRequest {
  return {
    id: `request-${index}`,
    displayNumber: `BF-${String(index).padStart(3, '0')}`,
    isFaq: false,
    status: 'pending',
    message: `Question ${index}`,
    helpType: 'physical',
    requestNumber: index,
    queuedAt: '2026-01-01T10:00:00Z',
    assignedTeacherName: null,
  };
}

function makeRequestWith(status: HelpRequestStatus, helpType: HelpType): HelpRequest {
  return {
    id: 'request-1',
    displayNumber: 'BF-001',
    isFaq: false,
    status,
    message: 'Question',
    helpType,
    requestNumber: 1,
    queuedAt: '2026-01-01T10:00:00Z',
    assignedTeacherName: null,
  };
}

interface SetupResult {
  fixture: ComponentFixture<StudentSession>;
  notifications: Subject<NotificationMessage>;
  reloads: {
    session: ReturnType<typeof vi.fn>;
    status: ReturnType<typeof vi.fn>;
    queue: ReturnType<typeof vi.fn>;
    requests: ReturnType<typeof vi.fn>;
    faq: ReturnType<typeof vi.fn>;
  };
}

describe('StudentSession', () => {
  let routerMock: { navigate: ReturnType<typeof vi.fn>; navigateByUrl: ReturnType<typeof vi.fn> };

  function setup(questionCount: number): SetupResult {
    routerMock = { navigate: vi.fn(), navigateByUrl: vi.fn() };

    const sessionResource = fakeResource({
      id: 'session-1',
      title: 'SWBAD',
      currentGeneration: 1,
      lastClearedAtUtc: null,
    });
    const statusResource = fakeResource({ waitingCount: 0, estimatedWaitMinutes: 0 });
    const requestsResource = fakeResource(
      Array.from({ length: questionCount }, (_, i) => makeRequest(i)),
    );
    const queueResource = fakeResource({ ownRequestId: null });
    const faqResource = fakeResource<unknown[]>([]);

    const sessionApi = {
      getStudentSession: () => sessionResource,
      getSessionStatus: () => statusResource,
      getStudentGenerations: () => signal([]),
    };

    const helpRequestApi = {
      getStudentRequests: () => requestsResource,
      getStudentQueue: () => queueResource,
      getStudentRequest: () => fakeResource(undefined),
      createHelpRequest: () => of(makeRequest(0)),
      addStudentMessage: () => of(undefined),
    };

    const faqApi = {
      getSessionFaq: () => faqResource,
    };

    const notifications = new Subject<NotificationMessage>();

    TestBed.configureTestingModule({
      providers: [
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: { snapshot: { params: { id: 'session-1' } } } },
        { provide: SessionApi, useValue: sessionApi },
        { provide: HelpRequestApi, useValue: helpRequestApi },
        { provide: FaqApi, useValue: faqApi },
        { provide: RealtimeHub, useValue: { notifications$: notifications.asObservable() } },
      ],
    });

    const fixture = TestBed.createComponent(StudentSession);
    fixture.componentRef.setInput('id', 'session-1');
    fixture.detectChanges();

    return {
      fixture,
      notifications,
      reloads: {
        session: sessionResource.reload as ReturnType<typeof vi.fn>,
        status: statusResource.reload as ReturnType<typeof vi.fn>,
        queue: queueResource.reload as ReturnType<typeof vi.fn>,
        requests: requestsResource.reload as ReturnType<typeof vi.fn>,
        faq: faqResource.reload as ReturnType<typeof vi.fn>,
      },
    };
  }

  describe('archiveVisibleCount', () => {
    it.each([
      { questions: 0, expected: 3 },
      { questions: 1, expected: 3 },
      { questions: 3, expected: 3 },
      { questions: 5, expected: 4 },
      { questions: 10, expected: 9 },
    ])(
      'returns $expected archive slots for $questions questions',
      ({ questions, expected }) => {
        const { fixture } = setup(questions);
        const component = fixture.componentInstance as unknown as {
          archiveVisibleCount: () => number;
        };
        expect(component.archiveVisibleCount()).toBe(expected);
      },
    );
  });

  describe('onQuestionSelected', () => {
    type ComponentAccess = {
      onQuestionSelected: (request: HelpRequest) => void;
      selectedRequest: () => HelpRequest | null;
    };

    it('opens the conversation dialog for completed digital requests', () => {
      const { fixture } = setup(0);
      const component = fixture.componentInstance as unknown as ComponentAccess;
      const request = makeRequestWith('completedByDigital', 'digital');

      component.onQuestionSelected(request);

      expect(component.selectedRequest()).toBe(request);
      expect(routerMock.navigate).not.toHaveBeenCalled();
    });

    it('navigates to the queue for pending requests', () => {
      const { fixture } = setup(0);
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.onQuestionSelected(makeRequestWith('pending', 'physical'));

      expect(routerMock.navigate).toHaveBeenCalledWith(['queue'], expect.anything());
    });

    it('navigates to the queue for active requests', () => {
      const { fixture } = setup(0);
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.onQuestionSelected(makeRequestWith('active', 'physical'));

      expect(routerMock.navigate).toHaveBeenCalledWith(['queue'], expect.anything());
    });

    it('navigates to the queue for awaiting confirmation requests', () => {
      const { fixture } = setup(0);
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.onQuestionSelected(makeRequestWith('awaitingConfirmation', 'digital'));

      expect(routerMock.navigate).toHaveBeenCalledWith(['queue'], expect.anything());
    });

    it('does nothing for completed physical requests', () => {
      const { fixture } = setup(0);
      const component = fixture.componentInstance as unknown as ComponentAccess;

      component.onQuestionSelected(makeRequestWith('completedByPhysical', 'physical'));

      expect(component.selectedRequest()).toBeNull();
      expect(routerMock.navigate).not.toHaveBeenCalled();
    });
  });

  describe('realtime events', () => {
    it('reloads status, queue and requests on SessionChanged', () => {
      const { notifications, reloads } = setup(0);

      notifications.next(makeNotification({ eventType: NotificationEventType.SessionChanged }));

      expect(reloads.status).toHaveBeenCalled();
      expect(reloads.queue).toHaveBeenCalled();
      expect(reloads.requests).toHaveBeenCalled();
    });

    it('reloads faq when the session change is FaqAnswered', () => {
      const { notifications, reloads } = setup(0);

      notifications.next(
        makeNotification({ eventType: NotificationEventType.SessionChanged, change: 'FaqAnswered' }),
      );

      expect(reloads.faq).toHaveBeenCalled();
    });

    it('reloads the session when the change is Cleared', () => {
      const { notifications, reloads } = setup(0);

      notifications.next(
        makeNotification({ eventType: NotificationEventType.SessionChanged, change: 'Cleared' }),
      );

      expect(reloads.session).toHaveBeenCalled();
    });

    it('ignores events from other sessions', () => {
      const { notifications, reloads } = setup(0);

      notifications.next(
        makeNotification({ eventType: NotificationEventType.SessionChanged, sessionId: 'other' }),
      );

      expect(reloads.status).not.toHaveBeenCalled();
      expect(reloads.queue).not.toHaveBeenCalled();
      expect(reloads.requests).not.toHaveBeenCalled();
      expect(reloads.faq).not.toHaveBeenCalled();
      expect(reloads.session).not.toHaveBeenCalled();
    });

    it('ignores non-SessionChanged events', () => {
      const { notifications, reloads } = setup(0);

      notifications.next(
        makeNotification({ eventType: NotificationEventType.HelpRequestCreated }),
      );

      expect(reloads.status).not.toHaveBeenCalled();
      expect(reloads.queue).not.toHaveBeenCalled();
      expect(reloads.requests).not.toHaveBeenCalled();
    });
  });
});
