import { httpResource, HttpResourceRef } from '@angular/common/http';
import { Service } from '@angular/core';
import { FaqItem } from '../faq-item.model';

interface FaqDto {
  id: string;
  question: string;
  answer: string | null;
}

@Service()
export class FaqApi {
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
}
