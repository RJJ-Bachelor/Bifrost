import { HttpResourceRef } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { signal } from '@angular/core';
import { Subject, of, throwError } from 'rxjs';
import {
  HelpRequestApi,
  type HelpRequest,
  type HelpRequestDetail,
  type HelpRequestMessage,
  type SessionQueue,
} from '../../../help-request';
import { SessionApi } from '../../../session';
import { RealtimeHub } from '../../../../core/realtime/services/realtime-hub';
import {
  NotificationEventType,
  type NotificationMessage,
} from '../../../../core/realtime/notification.model';
import { Queue } from './queue';

type QueueViewState = 'in-queue' | 'my-turn' | 'awaiting-confirmation' | 'completed-digital' | 'none';

interface ComponentAccess {
  viewState: () => QueueViewState;
  faqQuestion: () => string | null;
  faqAnswer: () => string | null;
  sendFollowUp: (text: string) => void;
  respondToFaq: (accept: boolean) => void;
  confirmCancel: () => void;
  sendingMessage: () => boolean;
  sendError: () => string | null;
  respondError: () => string | null;
  cancelError: () => string | null;
}

function access(component: Queue): ComponentAccess {
  return component as unknown as ComponentAccess;
}

function fakeResource<T>(initial: T): HttpResourceRef<T> {
  return {
    value: signal(initial).asReadonly(),
    isLoading: signal(false).asReadonly(),
    error: signal(null).asReadonly(),
    reload: vi.fn(),
  } as unknown as HttpResourceRef<T>;
}

function makeRequest(overrides: Partial<HelpRequest> = {}): HelpRequest {
  return {
    id: 'request-1',
    displayNumber: 'BF-001',
    status: 'pending',
    message: 'Hvordan starter jeg Docker?',
    helpType: 'physical',
    requestNumber: 1,
    queuedAt: '2026-01-01T10:00:00Z',
    assignedTeacherName: null,
    ...overrides,
  };
}

function makeQueue(overrides: Partial<SessionQueue> = {}): SessionQueue {
  return {
    sessionId: 'session-1',
    generation: 1,
    waitingCount: 1,
    unreservedWaitingCount: 1,
    ownRequestId: null,
    position: 1,
    estimatedWaitMinutes: 5,
    requestNumber: 1,
    displayNumber: 'BF-001',
    ...overrides,
  };
}

function makeMessage(overrides: Partial<HelpRequestMessage> = {}): HelpRequestMessage {
  return {
    id: 'message-1',
    authorRole: 'student',
    messageType: 'studentQuestion',
    text: 'Hej',
    createdAt: '2026-01-01T10:00:00Z',
    sequenceNumber: 1,
    ...overrides,
  };
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

interface SetupOptions {
  requests?: HelpRequest[];
  queue?: SessionQueue;
  detail?: HelpRequestDetail | undefined;
}

interface SetupResult {
  access: ComponentAccess;
  subject: Subject<NotificationMessage>;
  router: { navigate: ReturnType<typeof vi.fn> };
  reloadQueue: ReturnType<typeof vi.fn>;
  reloadRequests: ReturnType<typeof vi.fn>;
  addStudentMessage: ReturnType<typeof vi.fn>;
  respondToFaqApi: ReturnType<typeof vi.fn>;
  cancelRequest: ReturnType<typeof vi.fn>;
}

function setup(options: SetupOptions = {}): SetupResult {
  const requests = options.requests ?? [];
  const queue = options.queue ?? makeQueue();
  const detail = options.detail;
  const subject = new Subject<NotificationMessage>();

  const queueResource = fakeResource(queue);
  const requestsResource = fakeResource(requests);
  const reloadQueue = queueResource.reload as ReturnType<typeof vi.fn>;
  const reloadRequests = requestsResource.reload as ReturnType<typeof vi.fn>;

  const addStudentMessage = vi.fn(() => of({} as HelpRequestDetail));
  const respondToFaqApi = vi.fn(() => of({} as HelpRequestDetail));
  const cancelRequest = vi.fn(() => of(makeRequest()));

  const helpRequestApi = {
    getStudentQueue: vi.fn(() => queueResource),
    getStudentRequests: vi.fn(() => requestsResource),
    getStudentRequest: vi.fn(() => fakeResource(detail)),
    addStudentMessage,
    respondToFaq: respondToFaqApi,
    cancelRequest,
  };

  const sessionApi = {
    getStudentSession: vi.fn(() =>
      fakeResource({ id: 'session-1', title: 'SWBAD', currentGeneration: 1, lastClearedAtUtc: null }),
    ),
  };

  const router = { navigate: vi.fn() };

  TestBed.configureTestingModule({
    providers: [
      { provide: Router, useValue: router },
      { provide: ActivatedRoute, useValue: { snapshot: { params: { id: 'session-1' } } } },
      { provide: SessionApi, useValue: sessionApi },
      { provide: HelpRequestApi, useValue: helpRequestApi },
      { provide: RealtimeHub, useValue: { notifications$: subject.asObservable() } },
    ],
  });

  const fixture = TestBed.createComponent(Queue);
  fixture.componentRef.setInput('id', 'session-1');
  fixture.detectChanges();

  return {
    access: access(fixture.componentInstance),
    subject,
    router,
    reloadQueue,
    reloadRequests,
    addStudentMessage,
    respondToFaqApi,
    cancelRequest,
  };
}

describe('Queue', () => {
  describe('view state', () => {
    it('returns none when there is no active request', () => {
      const { access } = setup();
      expect(access.viewState()).toBe('none');
    });

    it.each<{ status: HelpRequest['status']; expected: QueueViewState }>([
      { status: 'active', expected: 'my-turn' },
      { status: 'awaitingConfirmation', expected: 'awaiting-confirmation' },
      { status: 'pending', expected: 'in-queue' },
      { status: 'awaitingFaq', expected: 'in-queue' },
      { status: 'completedByFaq', expected: 'none' },
      { status: 'cancelled', expected: 'none' },
    ])('maps $status to $expected', ({ status, expected }) => {
      const { access } = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest({ status })],
      });
      expect(access.viewState()).toBe(expected);
    });
  });

  describe('faq messages', () => {
    it('picks the most recent faqQuestion by sequence number', () => {
      const { access } = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest({ status: 'awaitingConfirmation' })],
        detail: {
          ...makeRequest({ status: 'awaitingConfirmation' }),
          messages: [
            makeMessage({ id: 'm-1', messageType: 'faqQuestion', text: 'old', sequenceNumber: 1 }),
            makeMessage({ id: 'm-2', messageType: 'faqQuestion', text: 'new', sequenceNumber: 3 }),
            makeMessage({ id: 'm-3', messageType: 'faqQuestion', text: 'mid', sequenceNumber: 2 }),
          ],
        },
      });
      expect(access.faqQuestion()).toBe('new');
    });

    it('picks the most recent faqAnswer by sequence number', () => {
      const { access } = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest({ status: 'awaitingConfirmation' })],
        detail: {
          ...makeRequest({ status: 'awaitingConfirmation' }),
          messages: [
            makeMessage({ id: 'm-1', messageType: 'faqAnswer', text: 'first', sequenceNumber: 1 }),
            makeMessage({ id: 'm-2', messageType: 'faqAnswer', text: 'second', sequenceNumber: 2 }),
          ],
        },
      });
      expect(access.faqAnswer()).toBe('second');
    });

    it('returns null when no faq messages exist', () => {
      const { access } = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest({ status: 'awaitingConfirmation' })],
        detail: {
          ...makeRequest({ status: 'awaitingConfirmation' }),
          messages: [makeMessage({ messageType: 'studentQuestion' })],
        },
      });
      expect(access.faqQuestion()).toBeNull();
      expect(access.faqAnswer()).toBeNull();
    });
  });

  describe('realtime events', () => {
    it('switches to completed-digital when HelpRequestAnswered targets our request', () => {
      const { access, subject } = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest({ status: 'pending' })],
      });
      expect(access.viewState()).toBe('in-queue');

      subject.next(
        makeNotification({
          eventType: NotificationEventType.HelpRequestAnswered,
          helpRequestId: 'request-1',
        }),
      );

      expect(access.viewState()).toBe('completed-digital');
    });

    it('ignores HelpRequestAnswered for foreign requests', () => {
      const { access, subject } = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest({ status: 'pending' })],
      });

      subject.next(
        makeNotification({
          eventType: NotificationEventType.HelpRequestAnswered,
          helpRequestId: 'someone-else',
        }),
      );

      expect(access.viewState()).toBe('in-queue');
    });

    it.each([
      NotificationEventType.SessionChanged,
      NotificationEventType.HelpRequestStarted,
      NotificationEventType.FaqAnswered,
    ])('reloads queue and requests on %s', (eventType) => {
      const { subject, reloadQueue, reloadRequests } = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest()],
      });

      subject.next(makeNotification({ eventType }));

      expect(reloadQueue).toHaveBeenCalled();
      expect(reloadRequests).toHaveBeenCalled();
    });

    it('ignores events from other sessions', () => {
      const { subject, reloadQueue, reloadRequests } = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest()],
      });

      subject.next(
        makeNotification({
          sessionId: 'other-session',
          eventType: NotificationEventType.SessionChanged,
        }),
      );

      expect(reloadQueue).not.toHaveBeenCalled();
      expect(reloadRequests).not.toHaveBeenCalled();
    });
  });

  describe('sendFollowUp', () => {
    function setupAfterAnswer(): SetupResult {
      const result = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest({ status: 'pending' })],
      });
      result.subject.next(
        makeNotification({
          eventType: NotificationEventType.HelpRequestAnswered,
          helpRequestId: 'request-1',
        }),
      );
      return result;
    }

    it('posts the message and reloads on success', () => {
      const { access, addStudentMessage, reloadQueue, reloadRequests } = setupAfterAnswer();

      access.sendFollowUp('any update?');

      expect(addStudentMessage).toHaveBeenCalledWith('request-1', 'any update?');
      expect(reloadQueue).toHaveBeenCalled();
      expect(reloadRequests).toHaveBeenCalled();
    });

    it('clears the answered state so the chat view closes', () => {
      const { access } = setupAfterAnswer();
      expect(access.viewState()).toBe('completed-digital');

      access.sendFollowUp('any update?');

      expect(access.viewState()).toBe('in-queue');
    });

    it('exposes an error when the request fails', () => {
      const { access, addStudentMessage } = setupAfterAnswer();
      addStudentMessage.mockReturnValueOnce(throwError(() => new Error('boom')));

      access.sendFollowUp('any update?');

      expect(access.sendingMessage()).toBe(false);
      expect(access.sendError()).toBe('Kunne ikke sende beskeden. Prøv igen.');
    });
  });

  describe('respondToFaq', () => {
    function setupAwaitingConfirmation(): SetupResult {
      return setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest({ status: 'awaitingConfirmation' })],
      });
    }

    it('navigates back to the session when the FAQ suggestion is accepted', () => {
      const { access, respondToFaqApi, router } = setupAwaitingConfirmation();

      access.respondToFaq(true);

      expect(respondToFaqApi).toHaveBeenCalledWith('request-1', true);
      expect(router.navigate).toHaveBeenCalledWith(['..'], expect.anything());
    });

    it('reloads the queue when the FAQ suggestion is rejected', () => {
      const { access, respondToFaqApi, router, reloadQueue, reloadRequests } = setupAwaitingConfirmation();

      access.respondToFaq(false);

      expect(respondToFaqApi).toHaveBeenCalledWith('request-1', false);
      expect(reloadQueue).toHaveBeenCalled();
      expect(reloadRequests).toHaveBeenCalled();
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('exposes an error when the request fails', () => {
      const { access, respondToFaqApi } = setupAwaitingConfirmation();
      respondToFaqApi.mockReturnValueOnce(throwError(() => new Error('boom')));

      access.respondToFaq(true);

      expect(access.respondError()).toBe('Kunne ikke svare på FAQ-forslaget. Prøv igen.');
    });
  });

  describe('confirmCancel', () => {
    it('cancels the request and navigates back to the session', () => {
      const { access, cancelRequest, router } = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest({ status: 'pending' })],
      });

      access.confirmCancel();

      expect(cancelRequest).toHaveBeenCalledWith('request-1');
      expect(router.navigate).toHaveBeenCalledWith(['..'], expect.anything());
    });

    it('exposes an error when the request fails', () => {
      const { access, cancelRequest } = setup({
        queue: makeQueue({ ownRequestId: 'request-1' }),
        requests: [makeRequest({ status: 'pending' })],
      });
      cancelRequest.mockReturnValueOnce(throwError(() => new Error('boom')));

      access.confirmCancel();

      expect(access.cancelError()).toBe('Kunne ikke trække dig ud af køen. Prøv igen.');
    });
  });
});
