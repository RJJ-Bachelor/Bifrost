import { HttpClient, httpResource, HttpResourceRef } from '@angular/common/http';
import { Service, inject, signal, Signal } from '@angular/core';
import { Observable } from 'rxjs';
import { Session, SessionGeneration, SessionStatus } from '../session.model';

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

  getStudentGenerations(id: () => string): Signal<SessionGeneration[]> {
    // TODO(BF-J2/J3): replace with real endpoint when available.
    // return httpResource<SessionGeneration[]>(() => ({
    //   url: `/api/students/sessions/${id()}/generations`,
    //   method: 'GET',
    //   withCredentials: true,
    // }), { defaultValue: [] });

    void id;
    void httpResource;

    return signal([
      { generation: 8, title: 'Lek 8 - SQL vs Mongo' },
      { generation: 7, title: 'Lek 7 - Cache og optimering' },
      { generation: 6, title: 'Lek 6 - Persistering af databaser' },
      { generation: 5, title: 'Lek 5 - REST API design' },
      { generation: 4, title: 'Lek 4 - Autentificering' },
    ]);
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
