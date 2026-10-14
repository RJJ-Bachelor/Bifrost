import { HttpClient } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Identity } from '../auth.model';

@Service()
export class AuthApi {
  private readonly http = inject(HttpClient);

  fetchTeacherIdentity(): Observable<Identity> {
    return this.http.get<Identity>('/api/teachers/me', { withCredentials: true });
  }

  fetchStudentIdentity(): Observable<Identity> {
    return this.http.get<Identity>('/api/students/me', { withCredentials: true });
  }
}
