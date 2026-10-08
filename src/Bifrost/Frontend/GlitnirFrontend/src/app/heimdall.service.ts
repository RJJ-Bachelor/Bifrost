import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export type IdentityKind = 'students' | 'teachers';
export interface Identity {
  userId: string;
  name?: string;
  roles: string[];
}

@Injectable({ providedIn: 'root' })
export class HeimdallService {
  private readonly http = inject(HttpClient);

  identity(kind: IdentityKind) {
    return this.http.get<Identity>(`/api/${kind}/me`, {
      withCredentials: true,
      observe: 'response'
    });
  }

  login() {
    const returnUrl = window.location.origin + '/teacher';
    window.location.assign('http://localhost:6004/account/login?returnUrl=' + encodeURIComponent(returnUrl));
  }
}
