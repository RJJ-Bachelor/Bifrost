import {HttpClient} from '@angular/common/http';
import {Service, inject} from '@angular/core';
import { Observable } from 'rxjs';
import { Identity, IdentityTransferCode } from '../auth.model';

@Service()
export class AuthApi {
  private readonly http = inject(HttpClient);

  fetchTeacherIdentity(): Observable<Identity> {
    return this.http.get<Identity>('/api/teachers/me', {withCredentials: true});
  }

  fetchStudentIdentity(): Observable<Identity> {
    return this.http.get<Identity>('/api/students/me', {withCredentials: true});
  }

  createIdentityTransferCode(): Observable<IdentityTransferCode> {
    return this.http.post<IdentityTransferCode>(
      '/api/students/identity-transfer-codes',
      {},
      { withCredentials: true },
    );
  }

  redeemIdentityTransferCode(code: string): Observable<void> {
    return this.http.post<void>(
      '/api/students/identity-transfer-codes/redeem',
      {code},
      {withCredentials: true},
    );
  }
}
