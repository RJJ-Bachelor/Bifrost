import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { tap } from 'rxjs';

export type IdentityKind = 'students' | 'teachers';
export interface Identity {
  userId: string;
  name?: string;
  roles: string[];
}

@Injectable({ providedIn: 'root' })
export class HeimdallService {
  private readonly http = inject(HttpClient);
  // A menu hint only. The API remains responsible for authentication and access.
  private readonly sessionKey = 'glitnir.logged-in';
  private readonly authenticated = signal(sessionStorage.getItem(this.sessionKey) === 'true');
  readonly loggedIn = this.authenticated.asReadonly();

  identity(kind: IdentityKind) {
    return this.http.get<Identity>(`/api/${kind}/me`, {
      withCredentials: true,
      observe: 'response'
    }).pipe(tap({
      next: response => {
        if (kind === 'teachers') this.setLoggedIn(response.body !== null);
      },
      error: (error: HttpErrorResponse) => {
        if (kind === 'teachers' && (error.status === 401 || error.status === 403)) {
          this.setLoggedIn(error.status === 403);
        }
      }
    }));
  }

  login() {
    const returnUrl = window.location.origin + '/teacher';
    window.location.assign('http://localhost:6004/account/login?returnUrl=' + encodeURIComponent(returnUrl));
  }

  logout() {
    this.setLoggedIn(false);
    const returnUrl = window.location.origin + '/';
    window.location.assign('http://localhost:6004/account/logout?returnUrl=' + encodeURIComponent(returnUrl));
  }

  private setLoggedIn(loggedIn: boolean) {
    this.authenticated.set(loggedIn);
    if (loggedIn) sessionStorage.setItem(this.sessionKey, 'true');
    else sessionStorage.removeItem(this.sessionKey);
  }
}
