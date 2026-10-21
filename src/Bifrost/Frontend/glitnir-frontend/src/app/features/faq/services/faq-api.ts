import { httpResource } from '@angular/common/http';
import { Service, signal, Signal } from '@angular/core';
import { FaqItem } from '../faq-item.model';

@Service()
export class FaqApi {
  getSessionFaq(sessionId: () => string, generation: () => number): Signal<FaqItem[]> {
    // TODO(): replace with real endpoint when available.
    // return httpResource<FaqItem[]>(() => ({
    //   url: `/api/students/sessions/${sessionId()}/generations/${generation()}/faq`,
    //   method: 'GET',
    //   withCredentials: true,
    // }), { defaultValue: [] });

    void sessionId;
    void generation;
    void httpResource;

    return signal([
      {
        id: '1',
        question: 'Hvordan starter jeg Docker?',
        answer: 'Kør `docker compose up` i projektroden. Containeren bygges og starter automatisk.',
        createdAtUtc: '2026-10-01T10:00:00Z',
      },
      {
        id: '2',
        question: 'Fejl: "Port 5432 already in use" ved Docker start',
        answer: 'Du har sandsynligvis en lokal Postgres kørende. Luk den ned med brew services stop postgresql eller skift port i docker-compose.yml til 5433:5432.',
        createdAtUtc: '2026-10-02T10:00:00Z',
      },
      {
        id: '3',
        question: 'Hvad er forskellen på Dockerfile og Docker Compose?',
        answer: 'En Dockerfile beskriver hvordan ét image bygges. Docker Compose beskriver hvordan flere containere kører sammen.',
        createdAtUtc: '2026-10-03T10:00:00Z',
      },
      {
        id: '4',
        question: 'Hvorfor forsvinder mine database-data, hver gang containeren genstartes?',
        answer: 'Du mangler en volume-mount i docker-compose.yml. Tilføj en named volume under services.postgres.volumes.',
        createdAtUtc: '2026-10-04T10:00:00Z',
      },
    ]);
  }
}
