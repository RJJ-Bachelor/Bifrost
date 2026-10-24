import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { Session } from '../session.model';
import { SessionApi } from './session-api';

describe('SessionApi', () => {
  let service: SessionApi;
  let http: HttpTestingController;

  const session: Session = {
    id: 'session-1',
    title: 'SWBAD-26',
    currentGeneration: 1,
    lastClearedAtUtc: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SessionApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  describe('joinSession', () => {
    it('posts the code to the student join endpoint', async () => {
      const promise = firstValueFrom(service.joinSession('ABC123'));

      const request = http.expectOne('/api/students/sessions/join');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ code: 'ABC123' });
      expect(request.request.withCredentials).toBe(true);
      request.flush(session);

      expect(await promise).toEqual(session);
    });
  });

  describe('joinTeacherSession', () => {
    it('posts the code to the teacher join endpoint', async () => {
      const promise = firstValueFrom(service.joinTeacherSession('ABC123'));

      const request = http.expectOne('/api/teachers/sessions/join');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ code: 'ABC123' });
      expect(request.request.withCredentials).toBe(true);
      request.flush(session);

      expect(await promise).toEqual(session);
    });
  });

  describe('createSession', () => {
    it('posts the title to the teacher sessions endpoint', async () => {
      const promise = firstValueFrom(service.createSession('SWBAD-26'));

      const request = http.expectOne('/api/teachers/sessions');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ title: 'SWBAD-26' });
      expect(request.request.withCredentials).toBe(true);
      request.flush(session);

      expect(await promise).toEqual(session);
    });
  });
});
