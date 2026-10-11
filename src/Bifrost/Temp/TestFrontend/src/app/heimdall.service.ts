import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs';

export interface Identity {
  userId: string;
  roles: string[];
  name?: string | null;
}

export interface SessionSummary {
  id: string;
  title: string;
  createdAtUtc: string;
  currentGeneration: number;
  lastClearedAtUtc: string | null;
}

export interface TeacherSession extends SessionSummary {
  code: string;
  createdBySubjectId: string;
  createdByDisplayName: string | null;
}

export interface ClearSessionResult {
  sessionId: string;
  currentGeneration: number;
  lastClearedAtUtc: string;
}

export interface HelpRequest {
  id: string;
  userId: string | null;
  message: string;
  sessionId: string | null;
  generation: number | null;
}

export interface ApiResponse<T> {
  status: number;
  body: T;
}

interface SelectedSession {
  id: string;
  title: string;
  code: string;
  generation: number;
}

@Injectable({ providedIn: 'root' })
export class HeimdallService {
  private readonly http = inject(HttpClient);
  private readonly storageKey = 'bifrost-test-session';
  session: SelectedSession = this.readSession();
  busy = false;

  get hasSessionId() {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(this.session.id.trim());
  }

  get hasGeneration() {
    return Number.isInteger(this.session.generation) && this.session.generation > 0;
  }

  get<T>(path: string) {
    return this.http.get('/api/' + path, { withCredentials: true, observe: 'response', responseType: 'text' })
      .pipe(map(response => ({ status: response.status, body: this.decodeBody(response.body) as T })));
  }

  post<T>(path: string, body: unknown) {
    return this.http.post('/api/' + path, body, { withCredentials: true, observe: 'response', responseType: 'text' })
      .pipe(map(response => ({ status: response.status, body: this.decodeBody(response.body) as T })));
  }

  decodeBody(body: unknown): unknown {
    if (typeof body !== 'string') return body;
    try { return JSON.parse(body); }
    catch { return body; }
  }

  selectSession(session: SessionSummary, code?: string) {
    this.session = {
      id: session.id,
      title: session.title,
      code: code ?? (this.session.id === session.id ? this.session.code : ''),
      generation: session.currentGeneration
    };
    this.saveSession();
  }

  changeSessionId(id: string) {
    this.session = { id, title: '', code: '', generation: 1 };
    this.saveSession();
  }

  resetSession() {
    this.session = { id: '', title: '', code: '', generation: 1 };
    this.saveSession();
  }

  saveSession() {
    try { sessionStorage.setItem(this.storageKey, JSON.stringify(this.session)); }
    catch { /* The current selection still works when browser storage is unavailable. */ }
  }

  login() {
    window.location.assign('http://localhost:6004/account/login?returnUrl=' + encodeURIComponent(window.location.origin + '/teachers'));
  }

  logout() {
    window.location.assign('http://localhost:6004/account/logout?returnUrl=' + encodeURIComponent(window.location.origin + '/teachers'));
  }

  private readSession(): SelectedSession {
    const empty = { id: '', title: '', code: '', generation: 1 };
    try {
      const value = JSON.parse(sessionStorage.getItem(this.storageKey) ?? 'null');
      if (!value) return empty;
      return {
        id: typeof value.id === 'string' ? value.id : '',
        title: typeof value.title === 'string' ? value.title : '',
        code: typeof value.code === 'string' ? value.code : '',
        generation: Number.isInteger(value.generation) && value.generation > 0 ? value.generation : 1
      };
    } catch { return empty; }
  }
}
