import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { HelpRequest } from '../help-request.model';
import { HelpRequestApi } from './help-request-api';

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
});
