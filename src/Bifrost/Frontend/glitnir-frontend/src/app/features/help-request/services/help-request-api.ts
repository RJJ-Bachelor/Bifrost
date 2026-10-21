import { HttpClient, httpResource, HttpResourceRef } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateHelpRequestInput, HelpRequest, SessionQueue } from '../help-request.model';

@Service()
export class HelpRequestApi {
  private readonly http = inject(HttpClient);

  getStudentQueue(sessionId: () => string): HttpResourceRef<SessionQueue> {
    return httpResource<SessionQueue>(() => ({
      url: `/api/students/sessions/${sessionId()}/queue`,
      method: 'GET',
      withCredentials: true,
    }), { defaultValue: { ownRequestId: null } });
  }

  createHelpRequest(sessionId: string, input: CreateHelpRequestInput): Observable<HelpRequest> {
    return this.http.post<HelpRequest>(
      `/api/students/sessions/${sessionId}/requests`,
      input,
      { withCredentials: true },
    );
  }
}
