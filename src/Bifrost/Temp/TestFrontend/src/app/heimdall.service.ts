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
  createdByTeacherId: string;
  createdByDisplayName: string | null;
}

export interface ClearSessionResult {
  sessionId: string;
  currentGeneration: number;
  lastClearedAtUtc: string;
}

export type HelpType = 'Physical' | 'Digital';
export type HelpRequestStatus = 'Pending' | 'Active' | 'AwaitingConfirmation' | 'Completed' | 'Cancelled';
export type HelpRequestAction = 'Start' | 'Complete' | 'Cancel';

export interface HelpRequestSummary {
  id: string;
  requestNumber: number;
  displayNumber: string;
  studentId: string;
  message: string;
  sessionId: string;
  generation: number;
  status: HelpRequestStatus;
  helpType: HelpType;
}

export interface HelpRequestMessage {
  id: string;
  helpRequestId: string;
  authorId: string;
  authorRole: 'Student' | 'Teacher';
  text: string;
  createdAt: string;
  sequenceNumber: number;
}

export interface HelpRequest {
  id: string;
  requestNumber: number;
  displayNumber: string;
  sessionId: string;
  generation: number;
  studentId: string;
  helpType: HelpType;
  status: HelpRequestStatus;
  assignedTeacherId: string | null;
  createdAt: string;
  queuedAt: string;
  returnedToQueueAt: string | null;
  startedAt: string | null;
  closedAt: string | null;
  faqAnswerId: string | null;
  messages: HelpRequestMessage[];
}

export interface StudentQueue {
  sessionId: string;
  generation: number;
  waitingCount: number;
  ownRequestId: string | null;
  requestNumber: number | null;
  displayNumber: string | null;
  position: number | null;
  estimatedWaitMinutes: number | null;
}

export interface TeacherQueue {
  sessionId: string;
  generation: number;
  waitingCount: number;
  requests: HelpRequest[];
  activeRequests: HelpRequest[];
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
  private requestRole: 'students' | 'teachers' = 'students';
  session: SelectedSession = this.readSession();
  requestId = this.readRequestId();
  busy = false;

  private get requestStorageKey() { return `bifrost-test-${this.requestRole}-request`; }

  constructor() {
    const url = new URL(window.location.href);
    const sessionId = url.searchParams.get('sessionId');
    const code = url.searchParams.get('code');
    if (sessionId && this.isGuid(sessionId) && code && /^[A-Z2-9]{8}$/i.test(code)) {
      this.changeSessionId(sessionId);
      this.session.code = code.toUpperCase();
      this.saveSession();
      url.searchParams.delete('sessionId');
      url.searchParams.delete('code');
      window.history.replaceState(window.history.state, '', url);
    }
  }

  get hasSessionId() {
    return this.isGuid(this.session.id);
  }

  get hasRequestId() {
    return this.isGuid(this.requestId);
  }

  get sessionLink() {
    if (!this.hasSessionId || !this.session.code.trim()) return '';
    const url = new URL('/students', window.location.origin);
    url.searchParams.set('sessionId', this.session.id.trim());
    url.searchParams.set('code', this.session.code.trim().toUpperCase());
    return url.toString();
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

  patch<T>(path: string, body: unknown) {
    return this.http.patch('/api/' + path, body, { withCredentials: true, observe: 'response', responseType: 'text' })
      .pipe(map(response => ({ status: response.status, body: this.decodeBody(response.body) as T })));
  }

  decodeBody(body: unknown): unknown {
    if (typeof body !== 'string') return body;
    try { return JSON.parse(body); }
    catch { return body; }
  }

  selectSession(session: SessionSummary, code?: string) {
    if (this.session.id !== session.id) this.selectRequest('');
    this.session = {
      id: session.id,
      title: session.title,
      code: code ?? (this.session.id === session.id ? this.session.code : ''),
      generation: session.currentGeneration
    };
    this.saveSession();
  }

  changeSessionId(id: string) {
    this.selectRequest('');
    this.session = { id, title: '', code: '', generation: 1 };
    this.saveSession();
  }

  resetSession() {
    this.selectRequest('');
    this.session = { id: '', title: '', code: '', generation: 1 };
    this.saveSession();
  }

  saveSession() {
    try { sessionStorage.setItem(this.storageKey, JSON.stringify(this.session)); }
    catch { /* The current selection still works when browser storage is unavailable. */ }
  }

  selectRequest(id: string) {
    this.requestId = id;
    this.saveRequestId();
  }

  useRequestRole(role: 'students' | 'teachers') {
    this.requestRole = role;
    this.requestId = this.readRequestId();
  }

  saveRequestId() {
    try { sessionStorage.setItem(this.requestStorageKey, this.requestId.trim()); }
    catch { /* The current selection still works without browser storage. */ }
  }

  login(returnPath = '/teachers') {
    window.location.assign('http://localhost:6004/account/login?returnUrl=' + encodeURIComponent(window.location.origin + returnPath));
  }

  logout() {
    window.location.assign('http://localhost:6004/account/logout?returnUrl=' + encodeURIComponent(window.location.origin + '/teachers'));
  }

  private isGuid(value: string) {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.trim());
  }

  private readRequestId() {
    try { return sessionStorage.getItem(this.requestStorageKey) ?? ''; }
    catch { return ''; }
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
