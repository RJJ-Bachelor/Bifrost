import { HttpClient, httpResource, HttpResourceRef } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable, delay, of } from 'rxjs';
import { FaqItem } from '../faq-item.model';

interface FaqDto {
  id: string;
  question: string;
  answer: string | null;
}

@Service()
export class FaqApi {
  private readonly http = inject(HttpClient);

  getSessionFaq(
    sessionId: () => string,
    generation: () => number,
  ): HttpResourceRef<FaqItem[]> {
    // TODO(BF-J2/J3): add generation to URL when backend supports per-generation FAQ.
    void generation;

    return httpResource<FaqItem[]>(
      () => ({
        url: `/api/students/sessions/${sessionId()}/faqs`,
        method: 'GET',
        withCredentials: true,
      }),
      {
        defaultValue: [],
        parse: (raw) =>
          (raw as FaqDto[]).map((item) => ({
            id: item.id,
            question: item.question,
            answer: item.answer ?? '',
          })),
      },
    );
  }

  getTeacherFaqs(sessionId: () => string): HttpResourceRef<FaqItem[]> {
    return httpResource<FaqItem[]>(
      () => ({
        url: `/api/teachers/sessions/${sessionId()}/faqs`,
        method: 'GET',
        withCredentials: true,
      }),
      {
        defaultValue: [],
        parse: (raw) =>
          (raw as FaqDto[])
            .filter((item) => item.answer !== null)
            .map((item) => ({
              id: item.id,
              question: item.question,
              answer: item.answer ?? '',
            })),
      },
    );
  }

  // TODO(BF-45): replace mocks with real endpoints when FAQ CRUD exists in backend.
  createFaq(sessionId: string, question: string, answer: string): Observable<FaqItem> {
    void sessionId;
    return of({ id: crypto.randomUUID(), question, answer }).pipe(delay(200));
  }

  updateFaq(faqId: string, question: string, answer: string): Observable<FaqItem> {
    return of({ id: faqId, question, answer }).pipe(delay(200));
  }

  deleteFaq(faqId: string): Observable<void> {
    void faqId;
    return of(undefined).pipe(delay(200));
  }
}
