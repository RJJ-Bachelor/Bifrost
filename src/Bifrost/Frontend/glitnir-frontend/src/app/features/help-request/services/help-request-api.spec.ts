import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { HelpRequest, HelpRequestStatus, TeacherStatusAction } from '../help-request.model';
import { HelpRequestApi } from './help-request-api';

interface RawRequest {
  id: string;
  displayNumber: string;
  status: string;
  message: string;
  helpType: string;
  requestNumber: number;
  queuedAt: string;
  assignedTeacherName: string | null;
}

function rawRequest(overrides: Partial<RawRequest> = {}): RawRequest {
  return {
    id: 'request-1',
    displayNumber: 'BF-001',
    status: 'Pending',
    message: 'Hvordan starter jeg Docker?',
    helpType: 'Physical',
    requestNumber: 1,
    queuedAt: '2026-01-01T10:00:00Z',
    assignedTeacherName: null,
    ...overrides,
  };
}

function rawDetail(overrides: Partial<RawRequest & { messages: unknown[] }> = {}) {
  return {
    ...rawRequest(),
    messages: [
      {
        id: 'message-1',
        authorRole: 'Student',
        messageType: 'StudentQuestion',
        text: 'Hvordan starter jeg Docker?',
        createdAt: '2026-01-01T10:00:00Z',
        sequenceNumber: 1,
      },
      {
        id: 'message-2',
        authorRole: 'Teacher',
        messageType: 'TeacherAnswer',
        text: 'Start Docker Desktop.',
        createdAt: '2026-01-01T10:01:00Z',
        sequenceNumber: 2,
      },
    ],
    ...overrides,
  };
}

describe('HelpRequestApi', () => {
  let service: HelpRequestApi;
  let http: HttpTestingController;

  const request: HelpRequest = {
    id: 'request-1',
    displayNumber: 'BF-001',
    status: 'pending',
    message: 'Hvordan starter jeg Docker?',
    helpType: 'physical',
    requestNumber: 1,
    queuedAt: '2026-01-01T10:00:00Z',
    assignedTeacherName: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HelpRequestApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  describe('createHelpRequest', () => {
    it('posts the input to the session requests endpoint', async () => {
      const promise = firstValueFrom(
        service.createHelpRequest('session-1', {
          message: 'Hvordan starter jeg Docker?',
          helpType: 'physical',
        }),
      );

      const req = http.expectOne('/api/students/sessions/session-1/requests');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({
        message: 'Hvordan starter jeg Docker?',
        helpType: 'physical',
      });
      expect(req.request.withCredentials).toBe(true);
      req.flush(request);

      expect(await promise).toEqual(request);
    });
  });

  describe('getStudentRequests', () => {
    it.each<[string, HelpRequestStatus]>([
      ['Pending', 'pending'],
      ['Active', 'active'],
      ['AwaitingConfirmation', 'awaitingConfirmation'],
      ['AwaitingFaq', 'awaitingFaq'],
      ['CompletedByFaq', 'completedByFaq'],
      ['CompletedByDigital', 'completedByDigital'],
      ['CompletedByPhysical', 'completedByPhysical'],
      ['Cancelled', 'cancelled'],
    ])('maps backend status %s to %s', async (rawStatus, expected) => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getStudentRequests(() => 'session-1'),
      );
      TestBed.tick();

      http
        .expectOne('/api/students/requests?sessionId=session-1')
        .flush([rawRequest({ status: rawStatus })]);

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()[0].status).toBe(expected);
    });

    it('maps queuedAt and assignedTeacherName', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getStudentRequests(() => 'session-1'),
      );
      TestBed.tick();

      http.expectOne('/api/students/requests?sessionId=session-1').flush([
        rawRequest({
          queuedAt: '2026-10-25T12:00:00Z',
          assignedTeacherName: 'Knud',
        }),
      ]);

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()[0].queuedAt).toBe('2026-10-25T12:00:00Z');
      expect(resource.value()[0].assignedTeacherName).toBe('Knud');
    });

    it('falls back to pending for unknown statuses', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      const resource = TestBed.runInInjectionContext(() =>
        service.getStudentRequests(() => 'session-1'),
      );
      TestBed.tick();

      http
        .expectOne('/api/students/requests?sessionId=session-1')
        .flush([rawRequest({ status: 'SomethingNew' })]);

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()[0].status).toBe('pending');
      expect(warn).toHaveBeenCalled();
      warn.mockRestore();
    });
  });

  describe('getStudentRequest', () => {
    it('fetches the detail and maps messages', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getStudentRequest(() => 'request-1'),
      );
      TestBed.tick();

      http.expectOne('/api/students/requests/request-1').flush(rawDetail());

      await TestBed.inject(ApplicationRef).whenStable();

      const detail = resource.value();
      expect(detail?.id).toBe('request-1');
      expect(detail?.messages).toHaveLength(2);
      expect(detail?.messages[0].authorRole).toBe('student');
      expect(detail?.messages[1].authorRole).toBe('teacher');
      expect(detail?.messages[1].messageType).toBe('teacherAnswer');
    });

    it('does not call the endpoint when the id is null', () => {
      TestBed.runInInjectionContext(() => service.getStudentRequest(() => null));
      TestBed.tick();

      http.expectNone('/api/students/requests/null');
    });
  });

  describe('getStudentQueue', () => {
    it('fetches the queue for the session', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getStudentQueue(() => 'session-1'),
      );
      TestBed.tick();

      const req = http.expectOne('/api/students/sessions/session-1/queue');
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBe(true);
      req.flush({ ownRequestId: 'request-1' });

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()).toEqual({ ownRequestId: 'request-1' });
    });
  });

  describe('getTeacherQueue', () => {
    it('maps the queue payload to the current model', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getTeacherQueue(() => 'session-1'),
      );
      TestBed.tick();

      http.expectOne('/api/teachers/sessions/session-1/queue').flush({
        generation: 8,
        waitingCount: 4,
        unreservedWaitingCount: 3,
        myReservedRequestId: null,
        requests: [
          { id: 'request-1', queuedAt: '2026-10-25T10:00:00Z', assignedTeacherId: null },
        ],
      });

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()).toEqual({
        waitingCount: 4,
        unreservedWaitingCount: 3,
        generation: 8,
        frontQueuedAt: '2026-10-25T10:00:00Z',
        myReservedRequestId: null,
      });
    });

    it('returns null frontQueuedAt when there are no waiting requests', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getTeacherQueue(() => 'session-1'),
      );
      TestBed.tick();

      http.expectOne('/api/teachers/sessions/session-1/queue').flush({
        generation: 8,
        waitingCount: 0,
        unreservedWaitingCount: 0,
        myReservedRequestId: null,
        requests: [],
      });

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value().frontQueuedAt).toBeNull();
    });

    it('tolerates a payload without a requests field', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getTeacherQueue(() => 'session-1'),
      );
      TestBed.tick();

      http.expectOne('/api/teachers/sessions/session-1/queue').flush({
        generation: 8,
        waitingCount: 0,
        unreservedWaitingCount: 0,
        myReservedRequestId: null,
      });

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value().frontQueuedAt).toBeNull();
    });
  });

  describe('cancelRequest', () => {
    it('patches the request status with a Cancel action', async () => {
      const promise = firstValueFrom(service.cancelRequest('request-1'));

      const req = http.expectOne('/api/students/requests/request-1/status');
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ action: 'Cancel' });
      expect(req.request.withCredentials).toBe(true);
      req.flush({ ...request, status: 'cancelled' });

      expect((await promise).status).toBe('cancelled');
    });
  });

  describe('respondToFaq', () => {
    it('patches the faq endpoint and maps the returned detail', async () => {
      const promise = firstValueFrom(service.respondToFaq('request-1', true));

      const req = http.expectOne('/api/students/requests/request-1/faq');
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ accept: true });
      expect(req.request.withCredentials).toBe(true);
      req.flush(rawDetail());

      const detail = await promise;
      expect(detail.messages).toHaveLength(2);
      expect(detail.messages[0].authorRole).toBe('student');
    });
  });

  describe('addStudentMessage', () => {
    it('posts the message and maps the returned detail', async () => {
      const promise = firstValueFrom(service.addStudentMessage('request-1', 'Follow up'));

      const req = http.expectOne('/api/students/requests/request-1/messages');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ text: 'Follow up' });
      expect(req.request.withCredentials).toBe(true);
      req.flush(rawDetail());

      const detail = await promise;
      expect(detail.id).toBe('request-1');
      expect(detail.messages[1].text).toBe('Start Docker Desktop.');
    });
  });

  describe('takeNext', () => {
    it('posts to the take-next endpoint and returns the request id', async () => {
      const promise = firstValueFrom(service.takeNext('session-1'));

      const req = http.expectOne(
        '/api/teachers/sessions/session-1/queue/take-next',
      );
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBe(true);
      req.flush({ id: 'request-1' });

      expect(await promise).toEqual({ id: 'request-1' });
    });

    it('returns null when the queue is empty', async () => {
      const promise = firstValueFrom(service.takeNext('session-1'));

      const req = http.expectOne(
        '/api/teachers/sessions/session-1/queue/take-next',
      );
      req.flush(null, { status: 204, statusText: 'No Content' });

      expect(await promise).toBeNull();
    });
  });

  describe('getTeacherRequest', () => {
    it('fetches the detail and maps messages', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getTeacherRequest(() => 'request-1'),
      );
      TestBed.tick();

      const req = http.expectOne('/api/teachers/requests/request-1');
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBe(true);
      req.flush(rawDetail());

      await TestBed.inject(ApplicationRef).whenStable();

      const detail = resource.value();
      expect(detail?.id).toBe('request-1');
      expect(detail?.messages).toHaveLength(2);
      expect(detail?.messages[0].authorRole).toBe('student');
      expect(detail?.messages[1].authorRole).toBe('teacher');
    });

    it('does not call the endpoint when the id is null', () => {
      TestBed.runInInjectionContext(() => service.getTeacherRequest(() => null));
      TestBed.tick();

      http.expectNone('/api/teachers/requests/null');
    });

    it('derives question from the student message when backend omits message field', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getTeacherRequest(() => 'request-1'),
      );
      TestBed.tick();

      const { message: _omitted, ...teacherDetail } = rawDetail();
      http.expectOne('/api/teachers/requests/request-1').flush(teacherDetail);

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()?.question).toBe('Hvordan starter jeg Docker?');
    });
  });

  describe('answerTeacherRequest', () => {
    it('posts text and optional question and maps the returned detail', async () => {
      const promise = firstValueFrom(
        service.answerTeacherRequest('request-1', 'A shared answer', 'Revised question'),
      );

      const req = http.expectOne('/api/teachers/requests/request-1/messages');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ text: 'A shared answer', question: 'Revised question' });
      expect(req.request.withCredentials).toBe(true);
      req.flush(rawDetail());

      const detail = await promise;
      expect(detail.id).toBe('request-1');
      expect(detail.messages[1].text).toBe('Start Docker Desktop.');
    });

    it('sends question as undefined when not provided', async () => {
      const promise = firstValueFrom(
        service.answerTeacherRequest('request-1', 'A private answer'),
      );

      const req = http.expectOne('/api/teachers/requests/request-1/messages');
      expect(req.request.body).toEqual({ text: 'A private answer', question: undefined });
      req.flush(rawDetail());

      await promise;
    });
  });

  describe('changeTeacherStatus', () => {
    it.each<[TeacherStatusAction, string]>([
      ['start', 'Start'],
      ['complete', 'Complete'],
      ['cancel', 'Cancel'],
    ])('maps %s to backend action %s', async (action, expected) => {
      const promise = firstValueFrom(service.changeTeacherStatus('request-1', action));

      const req = http.expectOne('/api/teachers/requests/request-1/status');
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ action: expected });
      expect(req.request.withCredentials).toBe(true);
      req.flush(rawDetail());

      await promise;
    });
  });
});
