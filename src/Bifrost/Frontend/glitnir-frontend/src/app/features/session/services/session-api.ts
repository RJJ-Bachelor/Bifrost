import { HttpClient, httpResource, HttpResourceRef } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Session } from '../session.model';

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

  joinSession(code: string): Observable<Session> {
    return this.http.post<Session>(
      '/api/students/sessions/join',
      { code },
      { withCredentials: true },
    );
  }
}
