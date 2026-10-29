import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ArchiveResult, Session } from '../session.model';
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

  describe('archiveSession', () => {
    it('posts expectedGeneration and omits title until backend supports it', async () => {
      const result: ArchiveResult = {
        id: 'archive-1',
        sessionId: 'session-1',
        generation: 1,
        currentGeneration: 2,
        archivedAtUtc: '2026-10-25T12:00:00Z',
      };
      const promise = firstValueFrom(service.archiveSession('session-1', 1, 'Lek 1'));

      const request = http.expectOne('/api/teachers/sessions/session-1/archive');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ expectedGeneration: 1 });
      expect(request.request.withCredentials).toBe(true);
      request.flush(result);

      expect(await promise).toEqual(result);
    });
  });

  describe('deleteSession', () => {
    it('deletes the session at the teacher endpoint', async () => {
      const promise = firstValueFrom(service.deleteSession('session-1'));

      const request = http.expectOne('/api/teachers/sessions/session-1');
      expect(request.request.method).toBe('DELETE');
      expect(request.request.withCredentials).toBe(true);
      request.flush(null);

      await expect(promise).resolves.toBeNull();
    });
  });

  describe('getStudentSession', () => {
    it('fetches the session from the student endpoint', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getStudentSession(() => 'session-1'),
      );
      TestBed.tick();

      const request = http.expectOne('/api/students/sessions/session-1');
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials).toBe(true);
      request.flush(session);

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()).toEqual(session);
    });
  });

  describe('getTeacherSession', () => {
    it('fetches the session from the teacher endpoint', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getTeacherSession(() => 'session-1'),
      );
      TestBed.tick();

      http.expectOne('/api/teachers/sessions/session-1').flush(session);

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()).toEqual(session);
    });
  });

  describe('getSessionStatus', () => {
    it('fetches the queue status for the session', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getSessionStatus(() => 'session-1'),
      );
      TestBed.tick();

      const request = http.expectOne('/api/students/sessions/session-1/queue/status');
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials).toBe(true);
      request.flush({ waitingCount: 3, estimatedWaitMinutes: 12 });

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()).toEqual({ waitingCount: 3, estimatedWaitMinutes: 12 });
    });

    it('returns the default status when the endpoint returns nulls', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getSessionStatus(() => 'session-1'),
      );
      TestBed.tick();

      http
        .expectOne('/api/students/sessions/session-1/queue/status')
        .flush({ waitingCount: 0, estimatedWaitMinutes: null });

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()).toEqual({ waitingCount: 0, estimatedWaitMinutes: null });
    });
  });

  describe('getTeacherStatistics', () => {
    it('fetches the statistics for the session', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getTeacherStatistics(() => 'session-1'),
      );
      TestBed.tick();

      const request = http.expectOne('/api/teachers/sessions/session-1/statistics');
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials).toBe(true);
      request.flush({
        physicalCount: 4,
        digitalCount: 6,
        helpedStudentCount: 8,
        averageWaitMinutes: 3.5,
      });

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()).toEqual({
        physicalCount: 4,
        digitalCount: 6,
        helpedStudentCount: 8,
        averageWaitMinutes: 3.5,
      });
    });
  });

  describe('getTeacherArchives', () => {
    it('fetches archives and replaces title with the generation label', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getTeacherArchives(() => 'session-1'),
      );
      TestBed.tick();

      http.expectOne('/api/teachers/sessions/session-1/archives').flush([
        {
          id: 'gen-8',
          sessionId: 'session-1',
          title: 'SWBAD-26',
          generation: 8,
          startedAtUtc: '2026-10-01T10:00:00Z',
          archivedAtUtc: '2026-10-08T10:00:00Z',
          privateConversationCount: 2,
          generalizedQuestionCount: 1,
        },
      ]);

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()[0].title).toBe('Generation 8');
    });
  });
});
