import { HttpClient } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateHelpRequestInput, HelpRequest } from '../help-request.model';

@Service()
export class HelpRequestApi {
  private readonly http = inject(HttpClient);

  createHelpRequest(sessionId: string, input: CreateHelpRequestInput): Observable<HelpRequest> {
    return this.http.post<HelpRequest>(
      `/api/students/sessions/${sessionId}/requests`,
      input,
      { withCredentials: true },
    );
  }
}
