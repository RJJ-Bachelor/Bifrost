import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable, finalize, timeout } from 'rxjs';
import { ApiResponse, ClearSessionResult, HeimdallService, HelpRequest, Identity, SessionSummary, TeacherSession } from './heimdall.service';

@Component({
  selector: 'app-test-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './test-page.component.html'
})
export class TestPageComponent {
  readonly api = inject(HeimdallService);
  private readonly destroyRef = inject(DestroyRef);
  readonly page = inject(ActivatedRoute).snapshot.data['page'] as string;
  readonly titles: Record<string, string> = {
    overview: 'EirService', students: 'Studerende', requests: 'Hjælpeanmodninger', teachers: 'Lærer'
  };
  identity: Identity | null = null;
  studentSessions: SessionSummary[] | null = null;
  teacherSessions: TeacherSession[] | null = null;
  requests: HelpRequest[] | null = null;
  requestScope = 'Mine hjælpeanmodninger';
  sessionTitle = 'Testsession';
  requestId: string = crypto.randomUUID();
  message = 'Jeg har brug for hjælp';
  useSession = true;
  endpoint = '';
  requestBody: unknown;
  result: unknown;
  status: number | null = null;
  error = '';
  notice = '';

  get busy() { return this.api.busy; }

  constructor() { this.load(); }

  load() {
    switch (this.page) {
      case 'students': this.loadIdentity('students'); break;
      case 'teachers': this.loadIdentity('teachers'); break;
      case 'requests': this.loadRequests(); break;
      default: this.loadAlive(); break;
    }
  }

  loadAlive() {
    const path = 'monitoring/alive';
    this.execute('GET', path, this.api.get<string>(path));
  }

  loadIdentity(role: 'students' | 'teachers') {
    const path = role + '/me';
    this.execute('GET', path, this.api.get<Identity>(path), identity => this.identity = identity);
  }

  loadStudentSessions() {
    const path = 'students/sessions';
    this.execute('GET', path, this.api.get<SessionSummary[]>(path), sessions => this.studentSessions = sessions);
  }

  joinSession() {
    const code = this.api.session.code.trim().toUpperCase();
    if (!code) return;
    const path = 'students/sessions/join';
    const body = { code };
    this.execute('POST', path, this.api.post<SessionSummary>(path, body), session => {
      this.api.selectSession(session, code);
      if (this.studentSessions !== null) {
        this.studentSessions = [session, ...this.studentSessions.filter(item => item.id !== session.id)];
      }
      this.notice = `Du er tilmeldt "${session.title}" som studerende.`;
    }, body);
  }

  loadStudentSession() {
    if (!this.api.hasSessionId) return;
    const path = 'students/sessions/' + this.api.session.id.trim();
    this.execute('GET', path, this.api.get<SessionSummary>(path), session => this.chooseSession(session));
  }

  loadTeacherSessions() {
    const path = 'teachers/sessions';
    this.execute('GET', path, this.api.get<TeacherSession[]>(path), sessions => this.teacherSessions = sessions);
  }

  createSession() {
    const title = this.sessionTitle.trim();
    if (!title) return;
    const path = 'teachers/sessions';
    const body = { title };
    this.execute('POST', path, this.api.post<TeacherSession>(path, body), session => {
      this.api.selectSession(session, session.code);
      if (this.teacherSessions !== null) {
        this.teacherSessions = [session, ...this.teacherSessions.filter(item => item.id !== session.id)];
      }
      this.notice = `Sessionen "${session.title}" er oprettet. Gå til Studerende og tilmeld med koden ${session.code}.`;
    }, body);
  }

  loadTeacherSession() {
    if (!this.api.hasSessionId) return;
    const path = 'teachers/sessions/' + this.api.session.id.trim();
    this.execute('GET', path, this.api.get<TeacherSession>(path), session => this.chooseSession(session, session.code));
  }

  clearSession() {
    if (!this.api.hasSessionId || !this.api.hasGeneration) return;
    const path = 'teachers/sessions/' + this.api.session.id.trim() + '/clear';
    const body = { expectedGeneration: this.api.session.generation };
    this.execute('POST', path, this.api.post<ClearSessionResult>(path, body), cleared => {
      this.api.session.generation = cleared.currentGeneration;
      this.api.saveSession();
      if (this.teacherSessions !== null) {
        this.teacherSessions = this.teacherSessions.map(session => session.id === cleared.sessionId
          ? { ...session, currentGeneration: cleared.currentGeneration, lastClearedAtUtc: cleared.lastClearedAtUtc }
          : session);
      }
      this.notice = `Sessionen er ryddet. Den valgte generation er nu ${cleared.currentGeneration}.`;
    }, body);
  }

  chooseSession(session: SessionSummary, code?: string) {
    this.api.selectSession(session, code);
    this.notice = `"${session.title}" er valgt. Sessionens ID og generation bruges i de næste kald.`;
  }

  loadRequests(forSession = false) {
    if (forSession && !this.api.hasSessionId) return;
    const path = 'students/requests' + (forSession ? '?sessionId=' + this.api.session.id.trim() : '');
    this.execute('GET', path, this.api.get<HelpRequest[]>(path), requests => {
      this.requests = requests;
      this.requestScope = forSession ? 'Mine hjælpeanmodninger i den valgte session' : 'Mine hjælpeanmodninger';
    });
  }

  newRequestId() { this.requestId = crypto.randomUUID(); }

  createHelpRequest() {
    if (!this.requestId.trim() || !this.message.trim()) return;
    if (this.useSession && (!this.api.hasSessionId || !this.api.hasGeneration)) return;
    const path = 'students/createhelprequest';
    const body = {
      id: this.requestId.trim(), message: this.message.trim(),
      ...(this.useSession ? { sessionId: this.api.session.id.trim(), generation: this.api.session.generation } : {})
    };
    this.execute('POST', path, this.api.post<string>(path, body), id => {
      this.notice = `Hjælpeanmodningen ${id} er oprettet. Hent listen for at se den.`;
      this.requests = null;
      this.newRequestId();
    }, body);
  }

  private execute<T>(method: 'GET' | 'POST', path: string, request: Observable<ApiResponse<T>>,
    onSuccess?: (body: T) => void, body?: unknown) {
    if (this.busy) return;
    this.endpoint = `${method} /api/${path}`;
    this.requestBody = body;
    this.result = undefined;
    this.status = null;
    this.error = '';
    this.notice = '';
    this.api.busy = true;
    request.pipe(timeout(30000), takeUntilDestroyed(this.destroyRef), finalize(() => this.api.busy = false)).subscribe({
      next: response => {
        this.status = response.status;
        this.result = response.body;
        onSuccess?.(response.body);
      },
      error: (failure: unknown) => {
        if (!(failure instanceof HttpErrorResponse)) {
          this.error = 'Kaldet tog mere end 30 sekunder eller blev afbrudt. Kontrollér at services kører.';
          return;
        }
        this.status = failure.status;
        this.result = this.api.decodeBody(failure.error);
        this.error = failure.status === 401
          ? path.startsWith('teachers/') ? 'Log ind som lærer, og prøv igen.' : 'Hent elevidentiteten på fanen Studerende, og prøv igen.'
          : failure.status === 403 ? 'Adgang afvist. Kontrollér kontoens rolle og rettigheder.'
          : failure.status === 404 ? 'Sessionen findes ikke, eller du har ikke adgang til den. Studerende skal først tilmelde sig.'
          : failure.status === 409 ? 'Sessionens generation er ændret. Hent sessionen igen, og prøv med den nye generation.'
          : failure.status === 0 || failure.status >= 502 && failure.status <= 504
            ? 'Kan ikke kontakte Heimdall eller EirService. Kontrollér at services kører.'
            : `Kaldet fejlede (HTTP ${failure.status}). Se API-svaret nedenfor.`;
      }
    });
  }
}
