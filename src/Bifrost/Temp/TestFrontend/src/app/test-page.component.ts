import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable, finalize, timeout } from 'rxjs';
import {
  ApiResponse, ClearSessionResult, HeimdallService, HelpRequest, HelpRequestAction, HelpRequestSummary,
  HelpType, Identity, SessionSummary, StudentQueue, TeacherQueue, TeacherSession
} from './heimdall.service';
import { StudentHelpRequestsComponent } from './student/help-requests/student-help-requests.component';
import { TeacherHelpRequestsComponent } from './teacher/help-requests/teacher-help-requests.component';

@Component({
  selector: 'app-test-page',
  imports: [CommonModule, FormsModule, StudentHelpRequestsComponent, TeacherHelpRequestsComponent],
  templateUrl: './test-page.component.html'
})
export class TestPageComponent {
  readonly api = inject(HeimdallService);
  private readonly destroyRef = inject(DestroyRef);
  readonly page = inject(ActivatedRoute).snapshot.data['page'] as string;
  readonly titles: Record<string, string> = {
    overview: 'EirService', students: 'Studerende: sessioner', teachers: 'Lærer: sessioner',
    studentRequests: 'Hjælpeanmodninger: studerende', teacherRequests: 'Hjælpeanmodninger: lærer'
  };
  identity: Identity | null = null;
  studentSessions: SessionSummary[] | null = null;
  teacherSessions: TeacherSession[] | null = null;
  requests: HelpRequestSummary[] | null = null;
  studentQueue: StudentQueue | null = null;
  teacherQueue: TeacherQueue | null = null;
  conversation: HelpRequest | null = null;
  replyText = '';
  requestScope = 'Mine hjælpeanmodninger';
  sessionTitle = 'Testsession';
  helpType: HelpType = 'Physical';
  message = 'Jeg har brug for hjælp';
  endpoint = '';
  requestBody: unknown;
  result: unknown;
  status: number | null = null;
  error = '';
  notice = '';

  get busy() { return this.api.busy; }

  get isTeacher() { return this.page === 'teachers' || this.page === 'teacherRequests'; }

  get hasStudentSession() {
    return this.studentSessions?.some(session => session.id === this.api.session.id.trim()) ?? false;
  }

  get hasTeacherSession() {
    return this.teacherSessions?.some(session => session.id === this.api.session.id.trim()) ?? false;
  }

  get currentRequest() {
    return this.conversation?.id === this.api.requestId.trim() ? this.conversation : null;
  }

  get canAct() {
    const request = this.currentRequest;
    return !!request && request.sessionId === this.api.session.id.trim() && request.generation === this.api.session.generation;
  }

  get isAssignedTeacher() {
    return !!this.identity && this.currentRequest?.assignedTeacherId === this.identity.userId;
  }

  get canStart() {
    return this.canAct && this.isAssignedTeacher && this.currentRequest?.helpType === 'Physical'
      && this.currentRequest.status === 'Pending';
  }

  get canComplete() {
    return this.canAct && this.isAssignedTeacher && this.currentRequest?.helpType === 'Physical'
      && this.currentRequest.status === 'Active';
  }

  get canSendMessage() {
    const request = this.currentRequest;
    if (!this.canAct || !request) return false;
    return this.isTeacher
      ? this.isAssignedTeacher && request.helpType === 'Digital' && request.status === 'Pending'
      : request.status === 'Completed' || request.status === 'Pending' && request.assignedTeacherId === null;
  }

  get canCancel() {
    const request = this.currentRequest;
    if (!this.canAct || !request || request.status === 'Completed' || request.status === 'Cancelled') return false;
    return this.isTeacher
      ? request.assignedTeacherId === null || this.isAssignedTeacher
      : request.status !== 'Active';
  }

  constructor() {
    this.api.useRequestRole(this.isTeacher ? 'teachers' : 'students');
    this.load();
  }

  load() {
    switch (this.page) {
      case 'students': this.loadIdentity('students'); break;
      case 'teachers': this.loadIdentity('teachers'); break;
      case 'studentRequests': this.loadIdentity('students', () => this.loadStudentSessions(true)); break;
      case 'teacherRequests': this.loadIdentity('teachers', () => this.loadTeacherSessions(true)); break;
      default: this.loadAlive(); break;
    }
  }

  loadAlive() {
    const path = 'monitoring/alive';
    this.execute('GET', path, this.api.get<string>(path));
  }

  loadIdentity(role: 'students' | 'teachers', onLoaded?: () => void) {
    const path = role + '/me';
    this.execute('GET', path, this.api.get<Identity>(path), identity => {
      this.identity = identity;
      onLoaded?.();
    });
  }

  loadStudentSessions(forHelpPage = false) {
    const path = 'students/sessions';
    this.execute('GET', path, this.api.get<SessionSummary[]>(path), sessions => {
      this.studentSessions = sessions;
      if (forHelpPage) {
        const selected = sessions.find(session => session.id === this.api.session.id.trim()) ?? sessions[0];
        if (selected) this.chooseSession(selected);
      }
    });
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
      if (this.page === 'studentRequests') this.chooseSession(session, code);
    }, body);
  }

  loadStudentSession() {
    if (!this.api.hasSessionId) return;
    const path = 'students/sessions/' + this.api.session.id.trim();
    this.execute('GET', path, this.api.get<SessionSummary>(path), session => this.chooseSession(session));
  }

  loadTeacherSessions(forHelpPage = false) {
    const path = 'teachers/sessions';
    this.execute('GET', path, this.api.get<TeacherSession[]>(path), sessions => {
      this.teacherSessions = sessions;
      if (forHelpPage) {
        const selected = sessions.find(session => session.id === this.api.session.id.trim()) ?? sessions[0];
        if (selected) this.chooseSession(selected, selected.code);
      }
    });
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
      this.updateGeneration(cleared.currentGeneration);
      this.api.selectRequest('');
      this.conversation = null;
      this.teacherQueue = null;
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
    this.requests = null;
    this.studentQueue = null;
    this.teacherQueue = null;
    this.conversation = null;
    this.replyText = '';
    this.notice = `"${session.title}" er valgt. Sessionens ID og generation bruges i de næste kald.`;
    if (this.page === 'studentRequests') this.loadRequests(true);
    if (this.page === 'teacherRequests') this.loadQueue();
  }

  loadRequests(forSession = false) {
    if (forSession && !this.api.hasSessionId) return;
    const path = 'students/requests' + (forSession ? '?sessionId=' + this.api.session.id.trim() : '');
    this.execute('GET', path, this.api.get<HelpRequestSummary[]>(path), requests => {
      this.requests = requests;
      this.requestScope = forSession ? 'Mine hjælpeanmodninger i den valgte session' : 'Mine hjælpeanmodninger';
    });
  }

  createHelpRequest() {
    if (!this.message.trim() || !this.hasStudentSession) return;
    const path = `students/sessions/${this.api.session.id.trim()}/requests`;
    const body = { message: this.message.trim(), helpType: this.helpType };
    this.execute('POST', path, this.api.post<HelpRequest>(path, body), request => {
      this.updateGeneration(request.generation);
      this.studentQueue = null;
      this.showHelpRequest(request);
      this.notice = `Hjælpeanmodning ${request.displayNumber} er oprettet. Hent din køplacering nedenfor.`;
    }, body);
  }

  loadQueue() {
    if (!this.api.hasSessionId) return;
    const role = this.isTeacher ? 'teachers' : 'students';
    const path = `${role}/sessions/${this.api.session.id.trim()}/queue`;
    if (this.isTeacher) {
      this.execute('GET', path, this.api.get<TeacherQueue>(path), queue => {
        this.teacherQueue = queue;
        this.updateGeneration(queue.generation);
      });
    } else {
      this.execute('GET', path, this.api.get<StudentQueue>(path), queue => {
        this.studentQueue = queue;
        this.updateGeneration(queue.generation);
      });
    }
  }

  takeNext() {
    if (!this.hasTeacherSession || !this.isTeacher) return;
    const path = `teachers/sessions/${this.api.session.id.trim()}/queue/take-next`;
    this.execute('POST', path, this.api.post<HelpRequest | null>(path, null), request => {
      if (!request) {
        this.notice = 'Der er ingen ledige anmodninger at reservere. Allerede reserverede anmodninger kan stadig være i køen.';
        return;
      }
      this.updateGeneration(request.generation);
      this.showHelpRequest(request);
      this.notice = `Anmodning ${request.displayNumber} er reserveret til dig. Start fysisk hjælp, eller send et digitalt svar.`;
    });
  }

  loadHelpRequest(id = this.api.requestId) {
    this.api.selectRequest(id.trim());
    if (!this.api.hasRequestId) return;
    this.conversation = null;
    const role = this.isTeacher ? 'teachers' : 'students';
    const path = `${role}/requests/${this.api.requestId}`;
    this.execute('GET', path, this.api.get<HelpRequest>(path), request => this.showHelpRequest(request));
  }

  sendMessage() {
    if (!this.canSendMessage || !this.replyText.trim()) return;
    const role = this.isTeacher ? 'teachers' : 'students';
    const path = `${role}/requests/${this.api.requestId.trim()}/messages`;
    const body = { text: this.replyText.trim() };
    this.execute('POST', path, this.api.post<HelpRequest>(path, body), request => {
      this.studentQueue = null;
      this.showHelpRequest(request);
      this.replyText = '';
      this.notice = this.isTeacher
        ? 'Dit digitale svar er gemt, og anmodningen er afsluttet.'
        : 'Din besked er gemt. Anmodningen venter nu bagest i køen.';
    }, body);
  }

  changeStatus(action: HelpRequestAction) {
    if (action === 'Start' && !this.canStart || action === 'Complete' && !this.canComplete
      || action === 'Cancel' && !this.canCancel) return;
    const role = this.isTeacher ? 'teachers' : 'students';
    const path = `${role}/requests/${this.api.requestId.trim()}/status`;
    const body = { action };
    this.execute('PATCH', path, this.api.patch<HelpRequest>(path, body), request => {
      this.studentQueue = null;
      this.showHelpRequest(request);
      this.notice = action === 'Start' ? 'Fysisk hjælp er startet. Anmodningen er nu aktiv.'
        : action === 'Complete' ? 'Hjælpen er afsluttet.'
        : this.isTeacher ? `Anmodning ${request.displayNumber} venter nu forrest i køen og kan tages igen.`
        : 'Anmodningen er annulleret. Eleven kan oprette en ny, hvis der stadig er brug for hjælp.';
    }, body);
  }

  private updateGeneration(generation: number) {
    const sessionId = this.api.session.id.trim();
    this.api.session.generation = generation;
    this.api.saveSession();
    this.studentSessions = this.studentSessions?.map(session => session.id === sessionId
      ? { ...session, currentGeneration: generation } : session) ?? null;
    this.teacherSessions = this.teacherSessions?.map(session => session.id === sessionId
      ? { ...session, currentGeneration: generation } : session) ?? null;
    this.requests = this.requests?.filter(request => request.sessionId !== sessionId || request.generation === generation) ?? null;
  }

  private showHelpRequest(request: HelpRequest) {
    if (this.conversation?.id !== request.id) this.replyText = '';
    this.api.selectRequest(request.id);
    this.conversation = request;
    if (!this.isTeacher) {
      if (this.requests !== null && (this.requests.some(item => item.id === request.id)
        || request.sessionId === this.api.session.id.trim() && request.generation === this.api.session.generation)) {
        const summary: HelpRequestSummary = {
          id: request.id, studentId: request.studentId, message: request.messages[0]?.text ?? '',
          requestNumber: request.requestNumber, displayNumber: request.displayNumber,
          sessionId: request.sessionId, generation: request.generation, status: request.status, helpType: request.helpType
        };
        this.requests = [summary, ...this.requests.filter(item => item.id !== request.id)];
      }
    }
    if (this.teacherQueue?.sessionId === request.sessionId && this.teacherQueue.generation === request.generation) {
      const requests = this.teacherQueue.requests.filter(item => item.id !== request.id);
      const activeRequests = this.teacherQueue.activeRequests.filter(item => item.id !== request.id);
      if (request.status === 'Pending') requests.push(request);
      if (request.status === 'Active') activeRequests.push(request);
      requests.sort((left, right) => (right.returnedToQueueAt ?? '').localeCompare(left.returnedToQueueAt ?? '')
        || left.queuedAt.localeCompare(right.queuedAt) || left.id.localeCompare(right.id));
      this.teacherQueue = { ...this.teacherQueue, requests, activeRequests, waitingCount: requests.length };
    }
  }

  private execute<T>(method: 'GET' | 'POST' | 'PATCH', path: string, request: Observable<ApiResponse<T>>,
    onSuccess?: (body: T) => void, body?: unknown) {
    if (this.busy) return;
    this.endpoint = `${method} /api/${path}`;
    this.requestBody = body;
    this.result = undefined;
    this.status = null;
    this.error = '';
    this.notice = '';
    this.api.busy = true;
    let successfulResponse: ApiResponse<T> | undefined;
    request.pipe(timeout(30000), takeUntilDestroyed(this.destroyRef), finalize(() => {
      this.api.busy = false;
      if (successfulResponse) onSuccess?.(successfulResponse.body);
    })).subscribe({
      next: response => {
        this.status = response.status;
        this.result = response.body;
        successfulResponse = response;
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
          : failure.status === 404 ? 'Sessionen eller anmodningen findes ikke, eller du har ikke adgang til den. Studerende skal først tilmelde sig sessionen.'
          : failure.status === 409 ? 'Handlingen er i konflikt med anmodningens eller sessionens tilstand. Se API-svaret og hent de aktuelle oplysninger.'
          : failure.status === 0 || failure.status >= 502 && failure.status <= 504
            ? 'Kan ikke kontakte Heimdall eller EirService. Kontrollér at services kører.'
            : `Kaldet fejlede (HTTP ${failure.status}). Se API-svaret nedenfor.`;
      }
    });
  }
}
