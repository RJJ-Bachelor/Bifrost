import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { FaqApi } from './faq-api';

describe('FaqApi', () => {
  let service: FaqApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FaqApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  describe('getSessionFaq', () => {
    it('fetches FAQ for the session', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getSessionFaq(() => 'session-1', () => 1),
      );
      TestBed.tick();

      const req = http.expectOne('/api/students/sessions/session-1/faqs');
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBe(true);
      req.flush([
        {
          id: 'faq-1',
          question: 'Hvordan starter jeg Docker?',
          answer: 'Kør docker compose up.',
        },
      ]);

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()).toEqual([
        {
          id: 'faq-1',
          question: 'Hvordan starter jeg Docker?',
          answer: 'Kør docker compose up.',
        },
      ]);
    });

    it('maps null answer to empty string', async () => {
      const resource = TestBed.runInInjectionContext(() =>
        service.getSessionFaq(() => 'session-1', () => 1),
      );
      TestBed.tick();

      const req = http.expectOne('/api/students/sessions/session-1/faqs');
      req.flush([
        { id: 'faq-1', question: 'Ubesvaret spørgsmål?', answer: null },
      ]);

      await TestBed.inject(ApplicationRef).whenStable();

      expect(resource.value()).toEqual([
        { id: 'faq-1', question: 'Ubesvaret spørgsmål?', answer: '' },
      ]);
    });
  });

  describe('createFaq', () => {
    it('posts the question and answer to the teacher endpoint', async () => {
      const promise = firstValueFrom(
        service.createFaq('session-1', 'Hvad er Docker?', 'Et container-værktøj.'),
      );

      const req = http.expectOne('/api/teachers/sessions/session-1/faqs');
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBe(true);
      expect(req.request.body).toEqual({
        question: 'Hvad er Docker?',
        answer: 'Et container-værktøj.',
      });
      req.flush({
        id: 'faq-1',
        sessionId: 'session-1',
        generation: 1,
        question: 'Hvad er Docker?',
        answer: 'Et container-værktøj.',
        sourceCount: 1,
        createdAt: '2026-11-01T10:00:00Z',
        answeredAt: '2026-11-01T10:00:00Z',
      });

      expect(await promise).toEqual({
        id: 'faq-1',
        question: 'Hvad er Docker?',
        answer: 'Et container-værktøj.',
      });
    });

    it('maps a null answer to an empty string', async () => {
      const promise = firstValueFrom(
        service.createFaq('session-1', 'Ubesvaret?', ''),
      );

      http.expectOne('/api/teachers/sessions/session-1/faqs').flush({
        id: 'faq-2',
        sessionId: 'session-1',
        generation: 1,
        question: 'Ubesvaret?',
        answer: null,
        sourceCount: 1,
        createdAt: '2026-11-01T10:00:00Z',
        answeredAt: null,
      });

      expect(await promise).toEqual({
        id: 'faq-2',
        question: 'Ubesvaret?',
        answer: '',
      });
    });
  });
});
