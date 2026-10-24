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
    it('fetches requests and maps PascalCase enums to lowercase', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getStudentRequests(() => 'session-1'),
      );
      TestBed.tick();

      const req = http.expectOne('/api/students/requests?sessionId=session-1');
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBe(true);
      req.flush([
        {
          id: 'request-1',
          displayNumber: 'BF-001',
          status: 'Pending',
          message: 'Hvordan starter jeg Docker?',
          helpType: 'Physical',
          requestNumber: 1,
        },
        {
          id: 'request-2',
          displayNumber: 'BF-002',
          status: 'Completed',
          message: 'Hvad er Docker Compose?',
          helpType: 'Digital',
          requestNumber: 2,
        },
      ]);

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()).toEqual([
        {
          id: 'request-1',
          displayNumber: 'BF-001',
          status: 'pending',
          message: 'Hvordan starter jeg Docker?',
          helpType: 'physical',
          requestNumber: 1,
        },
        {
          id: 'request-2',
          displayNumber: 'BF-002',
          status: 'completed',
          message: 'Hvad er Docker Compose?',
          helpType: 'digital',
          requestNumber: 2,
        },
      ]);
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
});
