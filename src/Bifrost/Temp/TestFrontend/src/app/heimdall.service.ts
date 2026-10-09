import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

@Injectable({ providedIn: 'root' })
export class HeimdallService {
  private readonly http = inject(HttpClient);
  get(path: string) { return this.http.get<unknown>('/api/' + path, { withCredentials: true }); }
  post(path: string, body: unknown) { return this.http.post<unknown>('/api/' + path, body, { withCredentials: true }); }
  login() { window.location.assign('http://localhost:6004/account/login?returnUrl=' + encodeURIComponent(window.location.origin + '/teachers')); }
  logout() { window.location.assign('http://localhost:6004/account/logout?returnUrl=' + encodeURIComponent(window.location.origin + '/teachers')); }
}
