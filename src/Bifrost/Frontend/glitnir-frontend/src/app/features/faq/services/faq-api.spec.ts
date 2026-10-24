import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
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
});
