import { HttpClient, httpResource, HttpResourceRef } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { FaqItem } from '../faq-item.model';

interface FaqDto {
  id: string;
  question: string;
  answer: string | null;
}

interface CreateFaqDto {
  id: string;
  sessionId: string;
  generation: number;
  question: string;
  answer: string | null;
  sourceCount: number;
  createdAt: string;
  answeredAt: string | null;
}

@Service()
export class FaqApi {
  private readonly http = inject(HttpClient);

  getSessionFaq(
    sessionId: () => string,
    generation: () => number,
  ): HttpResourceRef<FaqItem[]> {
    return httpResource<FaqItem[]>(
      () => {
        // The FAQ endpoint is per-session, not per-generation. Reading the
        // generation signal inside the request factory re-fetches the FAQ
        // automatically when the session advances to a new generation.
        void generation();
        return {
          url: `/api/students/sessions/${sessionId()}/faqs`,
          method: 'GET',
          withCredentials: true,
        };
      },
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

  createFaq(sessionId: string, question: string, answer: string): Observable<FaqItem> {
    return this.http
      .post<CreateFaqDto>(
        `/api/teachers/sessions/${sessionId}/faqs`,
        { question, answer },
        { withCredentials: true },
      )
      .pipe(
        map((dto) => ({
          id: dto.id,
          question: dto.question,
          answer: dto.answer ?? '',
        })),
      );
  }
}
