import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable, Subscription, catchError, debounceTime, finalize, forkJoin, interval, merge, of, throwError, timeout } from 'rxjs';
import { NotificationService } from './notification.service';
import {
  ClearSessionResult, HeimdallService, HelpRequest, HelpRequestAction, HelpRequestSummary,
  HelpType, Identity, SessionSummary, StudentQueue, TeacherQueue, TeacherSession
} from './heimdall.service';
import { StudentHelpRequestsComponent } from './student/help-requests/student-help-requests.component';
import { TeacherHelpRequestsComponent } from './teacher/help-requests/teacher-help-requests.component';

@Component({
  selector: 'app-test-page',
  imports: [CommonModule, FormsModule, RouterLink, StudentHelpRequestsComponent, TeacherHelpRequestsComponent],
  templateUrl: './test-page.component.html'
})
export class TestPageComponent {
  readonly api = inject(HeimdallService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly role = this.route.snapshot.data['role'] as 'students' | 'teachers';
  readonly isTeacher = this.role === 'teachers';
  sessionId = '';
  identity: Identity | null = null;
  sessions: (SessionSummary | TeacherSession)[] = [];
  session: SessionSummary | TeacherSession | null = null;
  requests: HelpRequestSummary[] = [];
  studentQueue: StudentQueue | null = null;
  teacherQueue: TeacherQueue | null = null;
  conversation: HelpRequest | null = null;
  joinCode = this.route.snapshot.queryParamMap.get('code') ?? '';
  sessionTitle = '';
  showCreateSession = false;
  helpType: HelpType = 'Physical';
  message = '';
  replyText = '';
  selectedRequestId = '';
  loading = true;
  busy = false;
  refreshing = false;
  loginRequired = false;
  sessionUnavailable = false;
  error = '';
  refreshError = '';
  notice = '';
  private refreshSubscription?: Subscription;
  private refreshQueued = false;
  private destroyed = false;

  get sessionCode() { return this.session && 'code' in this.session ? this.session.code : ''; }
  get teacherCode() { return this.session && 'teacherCode' in this.session ? this.session.teacherCode : ''; }
  get waitingRequests() { return (this.teacherQueue?.requests ?? []).filter(request => !request.assignedTeacherId); }
  get activeRequests() { return this.teacherQueue?.activeRequests ?? []; }
  get assignedRequests() { return [...(this.teacherQueue?.requests ?? []), ...this.activeRequests].filter(request => !!request.assignedTeacherId); }
  get myRequests() { return this.assignedRequests.filter(request => request.assignedTeacherId === this.identity?.userId); }
  get otherTeacherRequests() { return this.assignedRequests.filter(request => request.assignedTeacherId !== this.identity?.userId); }
  get hasOpenRequest() {
    return this.requests.some(request => ['Pending', 'Active', 'AwaitingConfirmation'].includes(request.status));
  }
  get canAct() { return !!this.conversation && this.conversation.generation === this.session?.currentGeneration; }
  get isAssignedTeacher() { return this.isTeacher && !!this.identity && this.conversation?.assignedTeacherId === this.identity.userId; }
  get canStart() {
    return this.canAct && this.isAssignedTeacher && this.conversation?.helpType === 'Physical' && this.conversation.status === 'Pending';
  }
  get canComplete() {
    return this.canAct && this.isAssignedTeacher && this.conversation?.helpType === 'Physical' && this.conversation.status === 'Active';
  }
  get canSendMessage() {
    const request = this.conversation;
    if (!this.canAct || !request) return false;
    return this.isTeacher
      ? this.isAssignedTeacher && request.helpType === 'Digital' && request.status === 'Pending'
      : request.status === 'Completed' || request.status === 'Pending' && request.assignedTeacherId === null;
  }
  get canCancel() {
    const request = this.conversation;
    if (!this.canAct || !request || ['Completed', 'Cancelled'].includes(request.status)) return false;
    return this.isTeacher ? this.isAssignedTeacher : request.status !== 'Active';
  }

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.stopRefresh();
    });
    merge(this.notifications.received, this.notifications.reconnected).pipe(
      debounceTime(150), takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => this.refresh());
    // Not every queue change produces a notification.
    interval(this.isTeacher ? 2000 : 10000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (!document.hidden && !this.refreshing) this.refresh();
    });
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      this.stopRefresh();
      this.sessionId = params.get('sessionId') ?? '';
      this.session = null;
      this.conversation = null;
      this.replyText = '';
      this.selectedRequestId = this.readSelectedRequest();
      this.loading = true;
      this.loadIdentity();
    });
  }

  loadIdentity() {
    this.api.get<Identity>(this.role + '/me').pipe(timeout(30000), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: identity => {
        this.identity = identity;
        this.loginRequired = false;
        this.error = '';
        void this.notifications.connect(this.role);
        this.refresh();
      },
      error: failure => { this.loading = false; this.handleError(failure); }
    });
  }

  refresh() {
    if (this.destroyed || !this.identity) return;
    if (this.busy || this.refreshing) { this.refreshQueued = true; return; }
    this.refreshQueued = false;
    this.refreshing = true;
    const base = this.role + '/sessions/' + this.sessionId;
    const selectedId = this.selectedRequestId;
    const optional = <T>(read: Observable<T>) => read.pipe(catchError((failure: unknown) =>
      failure instanceof HttpErrorResponse && failure.status === 404 ? of(null) : throwError(() => failure)));
    this.refreshSubscription = forkJoin({
      sessions: this.api.get<(SessionSummary | TeacherSession)[]>(this.role + '/sessions'),
      session: this.sessionId ? optional(this.api.get<SessionSummary | TeacherSession>(base)) : of(null),
      queue: this.sessionId ? optional(this.api.get<StudentQueue | TeacherQueue>(base + '/queue')) : of(null),
      requests: this.sessionId && !this.isTeacher
        ? optional(this.api.get<HelpRequestSummary[]>('students/requests?sessionId=' + this.sessionId)) : of(null),
      conversation: this.sessionId && selectedId
        ? optional(this.api.get<HelpRequest>(this.role + '/requests/' + selectedId)) : of(null)
    }).pipe(timeout(30000), takeUntilDestroyed(this.destroyRef), finalize(() => {
      this.refreshing = false;
      this.loading = false;
      if (this.refreshQueued && !this.destroyed) this.refresh();
    })).subscribe({
      next: data => {
        this.refreshError = '';
        this.sessions = data.sessions;
        this.session = data.session;
        this.sessionUnavailable = !!this.sessionId && !data.session;
        this.requests = data.requests ?? [];
        this.studentQueue = !this.isTeacher ? data.queue as StudentQueue | null : null;
        this.teacherQueue = this.isTeacher ? data.queue as TeacherQueue | null : null;
        this.conversation = data.conversation?.sessionId === this.sessionId ? data.conversation : null;
      },
      error: failure => this.handleError(failure, true)
    });
  }

  joinSession() {
    const code = this.joinCode.trim().toUpperCase();
    if (!/^[A-HJ-NP-Z2-9]{8}$/.test(code)) return;
    this.execute(this.api.post<SessionSummary | TeacherSession>(this.role + '/sessions/join', { code }), session => {
      this.joinCode = '';
      this.notice = 'Du er tilmeldt ' + session.title + '.';
      if (this.isTeacher) void this.router.navigate(['/teachers/sessions', session.id]);
    });
  }

  login() { this.api.login(this.router.url); }

  ownsSession(session: SessionSummary | TeacherSession) {
    return 'createdByTeacherId' in session && session.createdByTeacherId === this.identity?.userId;
  }

  createSession() {
    if (!this.sessionTitle.trim()) return;
    this.execute(this.api.post<TeacherSession>('teachers/sessions', { title: this.sessionTitle.trim() }), session => {
      void this.router.navigate(['/teachers/sessions', session.id]);
    });
  }

  createHelpRequest() {
    if (!this.session || this.hasOpenRequest || !this.message.trim()) return;
    this.execute(this.api.post<HelpRequest>('students/sessions/' + this.sessionId + '/requests',
      { message: this.message.trim(), helpType: this.helpType }), request => {
      this.selectRequest(request.id);
      this.message = '';
      this.notice = request.displayNumber + ' er oprettet.';
    });
  }

  takeNext() {
    if (!this.isTeacher || !this.session) return;
    this.execute(this.api.post<HelpRequest | null>('teachers/sessions/' + this.sessionId + '/queue/take-next', null), request => {
      if (request) {
        this.selectRequest(request.id);
        this.conversation = request;
        this.replyText = '';
        this.notice = 'Du har taget ' + request.displayNumber + '.';
      }
      else this.notice = 'Der er ingen ledige hjælpeanmodninger i køen.';
    });
  }

  openRequest(id: string) {
    this.stopRefresh();
    this.selectRequest(id);
    this.conversation = null;
    this.replyText = '';
    this.refresh();
  }

  sendMessage() {
    if (!this.canSendMessage || !this.replyText.trim()) return;
    this.execute(this.api.post<HelpRequest>(this.role + '/requests/' + this.selectedRequestId + '/messages',
      { text: this.replyText.trim() }), request => {
      this.conversation = request;
      this.replyText = '';
      this.notice = this.isTeacher ? 'Dit svar er sendt, og hjælpen er afsluttet.' : 'Din besked er sendt. Anmodningen er tilbage i køen.';
    });
  }

  changeStatus(action: HelpRequestAction) {
    if (action === 'Start' && !this.canStart || action === 'Complete' && !this.canComplete || action === 'Cancel' && !this.canCancel) return;
    this.execute(this.api.patch<HelpRequest>(this.role + '/requests/' + this.selectedRequestId + '/status', { action }), request => {
      this.conversation = request;
      this.notice = action === 'Start' ? 'Fysisk hjælp er startet.' : action === 'Complete' ? 'Hjælpen er afsluttet.'
        : this.isTeacher ? 'Anmodningen er sendt forrest i køen.' : 'Din anmodning er annulleret.';
    });
  }

  clearSession() {
    if (!this.session || !window.confirm('Ryd køen og hjælpeanmodningerne for alle undervisere i sessionen? Alle deltagere forbliver tilmeldt.')) return;
    this.execute(this.api.post<ClearSessionResult>('teachers/sessions/' + this.sessionId + '/clear',
      { expectedGeneration: this.session.currentGeneration }), () => {
      this.selectRequest('');
      this.conversation = null;
      this.notice = 'Sessionen er ryddet.';
    });
  }

  async copySessionLink(role: 'students' | 'teachers' = 'students') {
    const code = role === 'teachers' ? this.teacherCode : this.sessionCode;
    if (!this.isTeacher || !code) return;
    const link = new URL('/' + role, window.location.origin);
    link.searchParams.set('code', code);
    try {
      await navigator.clipboard.writeText(link.toString());
      this.notice = role === 'teachers' ? 'Underviserlinket er kopieret.' : 'Studenterlinket er kopieret.';
    } catch {
      this.notice = (role === 'teachers' ? 'Del underviserkoden: ' : 'Del studenterkoden: ') + code;
    }
  }

  assignmentLabel(request: HelpRequest) {
    return request.assignedTeacherId === this.identity?.userId ? 'Tildelt dig' : 'Tildelt en anden underviser';
  }

  requestStatusLabel(request: HelpRequest) {
    return request.status === 'Pending' && request.assignedTeacherId ? 'Reserveret' : this.statusLabel(request.status);
  }

  statusLabel(status: string) {
    return ({ Pending: 'Venter', Active: 'I gang', AwaitingConfirmation: 'Afventer dig', Completed: 'Afsluttet', Cancelled: 'Annulleret' } as Record<string, string>)[status] ?? status;
  }

  private execute<T>(request: Observable<T>, success: (body: T) => void) {
    if (this.busy) return;
    this.stopRefresh();
    this.busy = true;
    this.error = '';
    this.refreshError = '';
    this.notice = '';
    request.pipe(timeout(30000), takeUntilDestroyed(this.destroyRef), finalize(() => { this.busy = false; })).subscribe({
      next: body => { this.busy = false; success(body); this.refresh(); },
      error: failure => { this.handleError(failure); this.busy = false; this.refresh(); }
    });
  }

  private stopRefresh() { this.refreshQueued = false; this.refreshSubscription?.unsubscribe(); this.refreshing = false; }
  private selectRequest(id: string) {
    this.selectedRequestId = id;
    try { sessionStorage.setItem(this.selectionKey, id); } catch { /* Selection also works without storage. */ }
  }
  private get selectionKey() { return 'bifrost-' + this.role + '-' + this.sessionId + '-request'; }
  private readSelectedRequest() {
    try { return sessionStorage.getItem(this.selectionKey) ?? ''; } catch { return ''; }
  }
  private handleError(failure: unknown, background = false) {
    const status = failure instanceof HttpErrorResponse ? failure.status : 0;
    if (status === 401) {
      this.identity = null;
      this.loginRequired = this.isTeacher;
      void this.notifications.stop(this.role);
    }
    const message = status === 401 ? this.isTeacher ? '' : 'Din studenteridentitet kunne ikke hentes. Prøv at genindlæse siden.'
      : status === 403 ? 'Du har ikke adgang til denne handling.'
      : status === 404 ? 'Sessionen eller anmodningen findes ikke, eller du har ikke adgang.'
      : status === 409 ? 'Anmodningen eller sessionen er ændret. Oplysningerne opdateres; prøv igen.'
      : status === 400 ? 'Kontrollér de oplysninger, du har indtastet.'
      : 'Kunne ikke hente data. Kontrollér forbindelsen, og prøv igen.';
    if (background) this.refreshError = message;
    else this.error = message;
  }
}
