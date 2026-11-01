import { HttpClient, httpResource, HttpResourceRef } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ArchiveResult, Session, SessionGeneration, SessionStatus, TeacherStatistics } from '../session.model';

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

  getStudentGenerations(id: () => string): HttpResourceRef<SessionGeneration[]> {
    return httpResource<SessionGeneration[]>(
      () => ({
        url: `/api/students/sessions/${id()}/archives`,
        method: 'GET',
        withCredentials: true,
      }),
      { defaultValue: [] },
    );
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

  getTeacherStatistics(id: () => string): HttpResourceRef<TeacherStatistics> {
    return httpResource<TeacherStatistics>(
      () => ({
        url: `/api/teachers/sessions/${id()}/statistics`,
        method: 'GET',
        withCredentials: true,
      }),
      {
        defaultValue: {
          physicalCount: 0,
          digitalCount: 0,
          helpedStudentCount: 0,
          averageWaitMinutes: null,
        },
      },
    );
  }

  getSessionStatus(id: () => string): HttpResourceRef<SessionStatus> {
    return httpResource<SessionStatus>(
      () => ({
        url: `/api/students/sessions/${id()}/queue/status`,
        method: 'GET',
        withCredentials: true,
      }),
      { defaultValue: { waitingCount: 0, estimatedWaitMinutes: null } },
    );
  }

  deleteSession(id: string): Observable<void> {
    return this.http.delete<void>(`/api/teachers/sessions/${id}`, {
      withCredentials: true,
    });
  }

  archiveSession(id: string, expectedGeneration: number, title: string): Observable<ArchiveResult> {
    return this.http.post<ArchiveResult>(
      `/api/teachers/sessions/${id}/archive`,
      { expectedGeneration, title },
      { withCredentials: true },
    );
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
