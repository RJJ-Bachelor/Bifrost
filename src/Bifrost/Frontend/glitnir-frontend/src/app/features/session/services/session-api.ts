import { HttpClient, httpResource, HttpResourceRef } from '@angular/common/http';
import { Service, inject, signal, Signal } from '@angular/core';
import { Observable } from 'rxjs';
import { Session, SessionGeneration, SessionStatus, TeacherStatistics } from '../session.model';

@Service()
export class SessionApi {
  private readonly http = inject(HttpClient);

  getStudentSessions(): HttpResourceRef<Session[]> {
    return httpResource<Session[]>(
      () => ({
        url: '/api/students/sessions',
        method: 'GET',
        withCredentials: true,
      }),
      { defaultValue: [] },
    );
  }

  getTeacherSessions(): HttpResourceRef<Session[]> {
    return httpResource<Session[]>(
      () => ({
        url: '/api/teachers/sessions',
        method: 'GET',
        withCredentials: true,
      }),
      { defaultValue: [] },
    );
  }

  getStudentSession(id: () => string): HttpResourceRef<Session | undefined> {
    return httpResource<Session>(() => ({
      url: `/api/students/sessions/${id()}`,
      method: 'GET',
      withCredentials: true,
    }));
  }

  getTeacherSession(id: () => string): HttpResourceRef<Session | undefined> {
    return httpResource<Session>(() => ({
      url: `/api/teachers/sessions/${id()}`,
      method: 'GET',
      withCredentials: true,
    }));
  }

  getStudentGenerations(id: () => string): Signal<SessionGeneration[]> {
    // TODO(BF-J2/J3): replace with real endpoint when available.
    // return httpResource<SessionGeneration[]>(() => ({
    //   url: `/api/students/sessions/${id()}/archives`,
    //   method: 'GET',
    //   withCredentials: true,
    // }), { defaultValue: [] });

    void id;
    void httpResource;

    const mockArchives: SessionGeneration[] = [
      { id: '1', sessionId: 'mock', title: 'Generation 8', generation: 8, startedAtUtc: '2026-01-01T00:00:00Z', archivedAtUtc: '2026-01-01T00:00:00Z', privateConversationCount: 0, generalizedQuestionCount: 0 },
      { id: '2', sessionId: 'mock', title: 'Generation 7', generation: 7, startedAtUtc: '2026-01-01T00:00:00Z', archivedAtUtc: '2026-01-01T00:00:00Z', privateConversationCount: 0, generalizedQuestionCount: 0 },
      { id: '3', sessionId: 'mock', title: 'Generation 6', generation: 6, startedAtUtc: '2026-01-01T00:00:00Z', archivedAtUtc: '2026-01-01T00:00:00Z', privateConversationCount: 0, generalizedQuestionCount: 0 },
      { id: '4', sessionId: 'mock', title: 'Generation 5', generation: 5, startedAtUtc: '2026-01-01T00:00:00Z', archivedAtUtc: '2026-01-01T00:00:00Z', privateConversationCount: 0, generalizedQuestionCount: 0 },
      { id: '5', sessionId: 'mock', title: 'Generation 4', generation: 4, startedAtUtc: '2026-01-01T00:00:00Z', archivedAtUtc: '2026-01-01T00:00:00Z', privateConversationCount: 0, generalizedQuestionCount: 0 },
    ];
    return signal(mockArchives);
  }

  getTeacherArchives(id: () => string): HttpResourceRef<SessionGeneration[]> {
    return httpResource<SessionGeneration[]>(
      () => ({
        url: `/api/teachers/sessions/${id()}/archives`,
        method: 'GET',
        withCredentials: true,
      }),
      {
        defaultValue: [],
        // Backend Title is the session title (same for all generations). Show generation instead.
        parse: (raw) =>
          (raw as SessionGeneration[]).map((item) => ({
            ...item,
            title: `Generation ${item.generation}`,
          })),
      },
    );
  }

  getTeacherStatistics(id: () => string): Signal<TeacherStatistics> {
    // TODO(BF-35): replace with real endpoint when available.
    // return httpResource<TeacherStatistics>(() => ({
    //   url: `/api/teachers/sessions/${id()}/statistics`,
    //   method: 'GET',
    //   withCredentials: true,
    // }), { defaultValue: { physicalCount: 0, digitalCount: 0, helpedStudentCount: 0, averageWaitMinutes: null } });

    void id;
    return signal({
      physicalCount: 12,
      digitalCount: 2,
      helpedStudentCount: 11,
      averageWaitMinutes: 10,
    });
  }

  getSessionStatus(id: () => string): Signal<SessionStatus> {
    // TODO(BF-J7): replace with real endpoint when available.
    // return httpResource<SessionStatus>(() => ({
    //   url: `/api/students/sessions/${id()}/status`,
    //   method: 'GET',
    //   withCredentials: true,
    // }), { defaultValue: { waitingCount: 0, estimatedWaitMinutes: 0 } });

    return signal({ waitingCount: 4, estimatedWaitMinutes: 10 });
  }

  joinSession(code: string): Observable<Session> {
    return this.http.post<Session>(
      '/api/students/sessions/join',
      { code },
      { withCredentials: true },
    );
  }

  joinTeacherSession(code: string): Observable<Session> {
    return this.http.post<Session>(
      '/api/teachers/sessions/join',
      { code },
      { withCredentials: true },
    );
  }

  createSession(title: string): Observable<Session> {
    return this.http.post<Session>(
      '/api/teachers/sessions',
      { title },
      { withCredentials: true },
    );
  }
}
