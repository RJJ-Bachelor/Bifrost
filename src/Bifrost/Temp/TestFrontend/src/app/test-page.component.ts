import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable, Subject, Subscription, auditTime, catchError, combineLatest, filter, finalize, forkJoin, map, merge, of, switchMap, tap, throwError, timeout } from 'rxjs';
import { NotificationService } from './notification.service';
import { SessionRefresh, helpRequestId, notificationRefreshParts, visibleSessionRefresh } from './notification.model';
import {
  ArchiveSessionResult, ClearSessionResult, Faq, HeimdallService, HelpRequest, HelpRequestAction, HelpRequestSummary,
  HelpType, HelpRequestMessageType, Identity, SessionArchive, SessionArchiveSummary, SessionSummary, StudentQueue, TeacherQueue, TeacherSession, TeacherSessionListItem
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
  readonly messageTypeLabels: Record<HelpRequestMessageType, string> = {
    StudentQuestion: 'Studerendes spørgsmål',
    FollowUpQuestion: 'Opfølgende spørgsmål',
    FaqQuestion: 'Generaliseret FAQ-spørgsmål',
    FaqAnswer: 'Fælles FAQ-svar fra underviser',
    TeacherAnswer: 'Svar fra underviser'
  };
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly role = this.route.snapshot.data['role'] as 'students' | 'teachers';
  readonly isTeacher = this.role === 'teachers';
  sessionId = '';
  identity: Identity | null = null;
  sessions: (SessionSummary | TeacherSessionListItem)[] = [];
  session: SessionSummary | TeacherSession | null = null;
  requests: HelpRequestSummary[] = [];
  studentQueue: StudentQueue | null = null;
  teacherQueue: TeacherQueue | null = null;
  conversation: HelpRequest | null = null;
  faqs: Faq[] = [];
  archives: SessionArchiveSummary[] = [];
  archive: SessionArchive | null = null;
  selectedArchiveId = '';
  archiveLoading = false;
  archiveError = '';
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
  private archiveSubscription?: Subscription;
  private activeRefreshParts = SessionRefresh.None;
  private queuedRefreshParts = SessionRefresh.None;
  private notificationRefreshParts = SessionRefresh.None;
  private readonly refreshChanges = new Subject<SessionRefresh>();
  private destroyed = false;
  private sessionContextInitialized = false;
  private generalizationContext = '';
  pendingGeneralizationId = '';
  private generalizationAttempt?: { key: string; id: string };

  get generalizationPayload() {
    return { sessionId: this.sessionId, generation: this.session?.currentGeneration,
      helpRequestIds: this.waitingRequests.filter(request => !request.isFaq).slice(0, 2).map(request => request.id) };
  }
  get generalizationExample() { return JSON.stringify(this.generalizationPayload, null, 2); }
  get canTestGeneralization() { return this.generalizationPayload.helpRequestIds.length >= 2; }
  get sessionCode() { return this.session && 'code' in this.session ? this.session.code : ''; }
  get teacherCode() { return this.session && 'teacherCode' in this.session ? this.session.teacherCode : ''; }
  get waitingRequests() { return (this.teacherQueue?.requests ?? []).filter(request => !request.assignedTeacherId); }
  get activeRequests() { return this.teacherQueue?.activeRequests ?? []; }
  get assignedRequests() { return [...(this.teacherQueue?.requests ?? []), ...this.activeRequests].filter(request => !!request.assignedTeacherId); }
  get myRequests() { return this.assignedRequests.filter(request => request.assignedTeacherId === this.identity?.userId); }
  get otherTeacherRequests() { return this.assignedRequests.filter(request => request.assignedTeacherId !== this.identity?.userId); }
  get hasOpenRequest() {
    return this.requests.some(request => ['Pending', 'Active', 'AwaitingFaq', 'AwaitingConfirmation'].includes(request.status));
  }
  get canAct() { return !!this.conversation && this.conversation.generation === this.session?.currentGeneration; }
  get isAssignedTeacher() { return this.isTeacher && !!this.identity && this.conversation?.assignedTeacherId === this.identity.userId; }
  get canRespondToFaq() {
    const request = this.conversation;
    return !this.isTeacher && this.canAct && !!request && request.sessionId === this.sessionId &&
      request.studentId === this.identity?.userId && !!request.faqAnswerId && request.status === 'AwaitingConfirmation';
  }
  get faqResponsesAwaitingConfirmation() {
    return this.requests.filter(request => request.sessionId === this.sessionId &&
      request.generation === this.session?.currentGeneration && request.studentId === this.identity?.userId &&
      request.status === 'AwaitingConfirmation');
  }
  get selectedFaq() { return this.faqs.find(faq => faq.id === this.conversation?.faqAnswerId); }
  get faqQuestion() {
    return this.conversation?.messages.filter(message => message.messageType === 'FaqQuestion').at(-1)?.text
      ?? this.selectedFaq?.question;
  }
  get faqAnswer() {
    return this.conversation?.messages.filter(message => message.messageType === 'FaqAnswer').at(-1)?.text;
  }
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
      this.archiveSubscription?.unsubscribe();
    });
    merge(this.notifications.received.pipe(map(notification => notificationRefreshParts(notification, this.refreshContext))),
      this.notifications.reconnected.pipe(map(() => this.visibleRefreshParts)), this.refreshChanges).pipe(
      filter(parts => parts !== SessionRefresh.None),
      tap(parts => { this.notificationRefreshParts |= parts; }),
      auditTime(150), takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => {
      const parts = this.notificationRefreshParts;
      this.notificationRefreshParts = SessionRefresh.None;
      this.refresh(parts);
    });
    combineLatest([this.route.paramMap, this.route.queryParamMap]).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(([params, query]) => {
      const sessionId = params.get('sessionId') ?? '';
      const requestedId = helpRequestId(query.get('requestId'));
      if (this.sessionContextInitialized && sessionId === this.sessionId) {
        if (requestedId && requestedId !== this.selectedRequestId) this.openRequest(requestedId);
        return;
      }
      this.sessionContextInitialized = true;
      this.stopRefresh();
      this.sessionId = sessionId;
      this.session = null;
      this.conversation = null;
      this.archiveSubscription?.unsubscribe();
      this.archives = [];
      this.archive = null;
      this.selectedArchiveId = '';
      this.archiveLoading = false;
      this.archiveError = '';
      this.replyText = '';
      this.selectedRequestId = requestedId ?? this.readSelectedRequest();
      this.loading = true;
      this.loadIdentity();
    });
  }

  generalizeHelpRequests() {
    if (!this.isTeacher || !this.session || !this.canTestGeneralization || this.pendingGeneralizationId || this.busy) return;
    const payload = this.generalizationPayload;
    const key = JSON.stringify(payload);
    // Retry an uncertain HTTP result with the same event id.
    if (this.generalizationAttempt?.key !== key) this.generalizationAttempt = { key, id: crypto.randomUUID() };
    this.execute(this.api.post<{ generalizationId: string }>('teachers/development/generalizations/from-help-requests',
      { ...payload, generalizationId: this.generalizationAttempt.id }), result => {
      this.pendingGeneralizationId = result.generalizationId;
      this.notice = 'Generaliseringen er sendt til behandling. FAQ-spørgsmålet vises i køen, når Eir har modtaget det.';
    }, SessionRefresh.None);
  }

  loadIdentity() {
    this.api.get<Identity>(this.role + '/me').pipe(timeout(30000), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: identity => {
        this.identity = identity;
        this.loginRequired = false;
        this.error = '';
        void this.notifications.connect(this.role, identity.userId);
        this.refresh();
      },
      error: failure => { this.loading = false; this.handleError(failure); }
    });
  }

  private get refreshContext() {
    return { role: this.role, sessionId: this.sessionId, generation: this.session?.currentGeneration,
      selectedRequestId: this.selectedRequestId, conversationStatus: this.conversation?.status };
  }
  private get visibleRefreshParts() { return visibleSessionRefresh(this.refreshContext); }
  private scheduleRefresh(parts: SessionRefresh) { this.refreshChanges.next(parts); }

  refresh(parts = this.visibleRefreshParts) {
    parts &= this.visibleRefreshParts;
    if (this.destroyed || !this.identity || parts === SessionRefresh.None) return;
    if (this.busy || this.refreshing) {
      this.queuedRefreshParts |= parts;
      return;
    }
    parts |= this.queuedRefreshParts;
    this.queuedRefreshParts = SessionRefresh.None;
    this.activeRefreshParts = parts;
    this.refreshing = true;
    const read = (part: SessionRefresh) => (parts & part) !== 0;
    const base = this.role + '/sessions/' + this.sessionId;
    const selectedId = this.selectedRequestId;
    const optional = <T>(request: Observable<T>) => request.pipe(catchError((failure: unknown) =>
      failure instanceof HttpErrorResponse && failure.status === 404 ? of(null) : throwError(() => failure)));
    // A teacher queue already contains the complete pending and active conversations.
    const conversationFromQueue = this.isTeacher && read(SessionRefresh.Queue);
    if (read(SessionRefresh.Archive) && this.selectedArchiveId) this.openArchive(this.selectedArchiveId);
    this.refreshSubscription = forkJoin({
      sessions: read(SessionRefresh.Sessions) ? this.api.get<(SessionSummary | TeacherSessionListItem)[]>(this.role + '/sessions') : of(null),
      session: read(SessionRefresh.Session) ? optional(this.api.get<SessionSummary | TeacherSession>(base)) : of(null),
      faqs: read(SessionRefresh.Faqs) ? optional(this.api.get<Faq[]>(base + '/faqs')) : of(null),
      archives: read(SessionRefresh.Archives) ? optional(this.api.get<SessionArchiveSummary[]>(base + '/archives')) : of(null),
      queue: read(SessionRefresh.Queue) ? optional(this.api.get<StudentQueue | TeacherQueue>(base + '/queue')) : of(null),
      requests: read(SessionRefresh.Requests)
        ? optional(this.api.get<HelpRequestSummary[]>('students/requests?sessionId=' + this.sessionId)) : of(null),
      conversation: read(SessionRefresh.Conversation) && selectedId && !conversationFromQueue
        ? optional(this.api.get<HelpRequest>(this.role + '/requests/' + selectedId)) : of(null)
    }).pipe(switchMap(data => {
      if (!conversationFromQueue || !selectedId) return of(data);
      const queue = data.queue as TeacherQueue | null;
      const queued = [...(queue?.requests ?? []), ...(queue?.activeRequests ?? [])].find(request => request.id === selectedId);
      if (queued) return of({ ...data, conversation: queued });
      return read(SessionRefresh.Conversation)
        ? optional(this.api.get<HelpRequest>(this.role + '/requests/' + selectedId)).pipe(map(conversation => ({ ...data, conversation })))
        : of(data);
    }), timeout(30000), takeUntilDestroyed(this.destroyRef), finalize(() => {
      this.refreshing = false;
      this.activeRefreshParts = SessionRefresh.None;
      this.loading = false;
      const queued = this.queuedRefreshParts;
      this.queuedRefreshParts = SessionRefresh.None;
      if (queued !== SessionRefresh.None && !this.destroyed) this.refresh(queued);
    })).subscribe({
      next: data => {
        this.refreshError = '';
        if (data.sessions) this.sessions = data.sessions;
        if (read(SessionRefresh.Session)) {
          this.session = data.session;
          this.sessionUnavailable = !!this.sessionId && !data.session;
        }
        if (read(SessionRefresh.Requests)) this.requests = data.requests ?? [];
        if (read(SessionRefresh.Faqs)) this.faqs = data.faqs ?? [];
        if (read(SessionRefresh.Archives)) this.archives = data.archives ?? [];
        if (read(SessionRefresh.Queue)) {
          this.studentQueue = !this.isTeacher ? data.queue as StudentQueue | null : null;
          this.teacherQueue = this.isTeacher ? data.queue as TeacherQueue | null : null;
        }
        if (selectedId === this.selectedRequestId && (read(SessionRefresh.Conversation) || data.conversation))
          this.conversation = data.conversation?.sessionId === this.sessionId ? data.conversation : null;
        const context = this.sessionId + ':' + this.session?.currentGeneration;
        if (context !== this.generalizationContext) {
          this.generalizationContext = context;
          this.pendingGeneralizationId = '';
          this.generalizationAttempt = undefined;
        } else if (this.pendingGeneralizationId && (this.teacherQueue?.requests.some(request => request.id === this.pendingGeneralizationId)
          || this.faqs.some(faq => faq.id === this.pendingGeneralizationId))) {
          this.pendingGeneralizationId = '';
          this.generalizationAttempt = undefined;
          this.notice = 'Det fælles FAQ-spørgsmål er modtaget i Eir og kan besvares fra køen.';
        }
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
    }, this.isTeacher ? SessionRefresh.None : SessionRefresh.Sessions);
  }

  login() { this.api.login(this.router.url); }

  ownsSession(session: SessionSummary | TeacherSession) {
    return 'createdByTeacherId' in session && session.createdByTeacherId === this.identity?.userId;
  }

  teacherSessionListItem(session: SessionSummary | TeacherSessionListItem) {
    return this.isTeacher && 'waitingCount' in session ? session : null;
  }

  createSession() {
    if (!this.sessionTitle.trim()) return;
    this.execute(this.api.post<TeacherSession>('teachers/sessions', { title: this.sessionTitle.trim() }), session => {
      void this.router.navigate(['/teachers/sessions', session.id]);
    }, SessionRefresh.None);
  }

  createHelpRequest() {
    if (!this.session || this.hasOpenRequest || !this.message.trim()) return;
    this.execute(this.api.post<HelpRequest>('students/sessions/' + this.sessionId + '/requests',
      { message: this.message.trim(), helpType: this.helpType }), request => {
      this.selectRequest(request.id);
      this.applyRequest(request);
      this.message = '';
      this.notice = request.displayNumber + ' er oprettet.';
    }, SessionRefresh.Queue);
  }

  takeNext() {
    if (!this.isTeacher || !this.session) return;
    this.execute(this.api.post<HelpRequest | null>('teachers/sessions/' + this.sessionId + '/queue/take-next', null), request => {
      if (request) {
        this.selectRequest(request.id);
        this.applyRequest(request);
        this.replyText = '';
        this.notice = 'Du har taget ' + request.displayNumber + '.';
      }
      else this.notice = 'Der er ingen ledige hjælpeanmodninger i køen.';
    }, SessionRefresh.Queue);
  }

  openRequest(id: string) {
    this.selectRequest(id);
    this.conversation = null;
    this.replyText = '';
    const queued = [...(this.teacherQueue?.requests ?? []), ...this.activeRequests].find(request => request.id === id);
    if (this.isTeacher && queued && !this.refreshing) this.conversation = queued;
    else this.refresh(SessionRefresh.Conversation);
  }

  openFaqResponse(id: string) {
    this.openRequest(id);
    document.getElementById('conversation-title')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  showSessionFaqs() {
    document.getElementById('faq-title')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  sendMessage() {
    if (!this.canSendMessage || !this.replyText.trim()) return;
    this.execute(this.api.post<HelpRequest>(this.role + '/requests/' + this.selectedRequestId + '/messages',
      { text: this.replyText.trim() }), request => {
      this.applyRequest(request);
      this.replyText = '';
      this.notice = this.isTeacher ? 'Dit svar er sendt, og hjælpen er afsluttet.' : 'Din besked er sendt. Anmodningen er tilbage i køen.';
    }, SessionRefresh.Queue);
  }

  respondToFaq(accept: boolean) {
    if (!this.canRespondToFaq || this.busy) return;
    this.execute(this.api.patch<HelpRequest>('students/requests/' + this.conversation!.id + '/faq', { accept }), request => {
      this.applyRequest(request);
      this.notice = accept ? 'FAQ-svaret er accepteret. Din anmodning er afsluttet.'
        : request.helpType === 'Physical' ? 'Du er tilbage i køen til fysisk hjælp med din oprindelige køplads.'
          : 'Du er tilbage i køen med din oprindelige køplads.';
    }, SessionRefresh.Queue);
  }

  changeStatus(action: HelpRequestAction) {
    if (action === 'Start' && !this.canStart || action === 'Complete' && !this.canComplete || action === 'Cancel' && !this.canCancel) return;
    this.execute(this.api.patch<HelpRequest>(this.role + '/requests/' + this.selectedRequestId + '/status', { action }), request => {
      this.applyRequest(request);
      this.notice = action === 'Start' ? 'Fysisk hjælp er startet.' : action === 'Complete' ? 'Hjælpen er afsluttet.'
        : this.isTeacher ? 'Anmodningen er sendt forrest i køen.' : 'Din anmodning er annulleret.';
    }, SessionRefresh.Queue);
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

  archiveSession() {
    if (!this.isTeacher || !this.session || this.busy || !window.confirm(
      'Arkivér generation ' + this.session.currentGeneration + '? FAQ og personlige samtaler gemmes i arkivet, og den aktuelle kø tømmes. Alle deltagere forbliver tilmeldt.')) return;
    this.execute(this.api.post<ArchiveSessionResult>('teachers/sessions/' + this.sessionId + '/archive',
      { expectedGeneration: this.session.currentGeneration }), result => {
      this.selectRequest('');
      this.conversation = null;
      this.replyText = '';
      this.notice = 'Generation ' + result.generation + ' er arkiveret. Sessionen fortsætter i generation ' + result.currentGeneration + '.';
      this.closeArchive();
      this.selectedArchiveId = result.archiveId;
    });
  }

  openArchive(id: string) {
    if (!this.sessionId || this.busy) return;
    this.archiveSubscription?.unsubscribe();
    this.selectedArchiveId = id;
    this.archive = null;
    this.archiveError = '';
    this.archiveLoading = true;
    this.archiveSubscription = this.api.get<SessionArchive>(this.role + '/sessions/' + this.sessionId + '/archives/' + id).pipe(
      timeout(30000), takeUntilDestroyed(this.destroyRef), finalize(() => { this.archiveLoading = false; })
    ).subscribe({
      next: archive => { this.archive = archive; },
      error: failure => {
        this.archiveError = failure instanceof HttpErrorResponse && failure.status === 404
          ? 'Arkivet findes ikke, eller du har ikke adgang.' : 'Kunne ikke hente arkivet. Prøv igen.';
        if (failure instanceof HttpErrorResponse && failure.status === 401) this.handleError(failure);
      }
    });
  }

  closeArchive() {
    this.archiveSubscription?.unsubscribe();
    this.selectedArchiveId = '';
    this.archive = null;
    this.archiveError = '';
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
    return ({ Pending: 'Venter', Active: 'I gang', AwaitingFaq: 'Afventer fælles FAQ-svar', AwaitingConfirmation: 'FAQ-svar klar – svar påkrævet', Completed: 'Afsluttet', Cancelled: 'Annulleret' } as Record<string, string>)[status] ?? status;
  }

  private applyRequest(request: HelpRequest) {
    this.conversation = request;
    if (this.isTeacher || !request.studentId || request.generation !== this.session?.currentGeneration) return;
    const previous = this.requests.find(summary => summary.id === request.id);
    const summary: HelpRequestSummary = {
      id: request.id, requestNumber: request.requestNumber, displayNumber: request.displayNumber,
      studentId: request.studentId, sessionId: request.sessionId, generation: request.generation,
      status: request.status, helpType: request.helpType,
      message: request.messages.find(message => message.messageType === 'StudentQuestion')?.text ?? previous?.message ?? ''
    };
    this.requests = previous ? this.requests.map(item => item.id === request.id ? summary : item) : [...this.requests, summary];
  }

  private execute<T>(request: Observable<T>, success: (body: T) => void, parts = this.visibleRefreshParts) {
    if (this.busy) return;
    const interrupted = this.activeRefreshParts | this.queuedRefreshParts;
    this.stopRefresh();
    this.queuedRefreshParts = interrupted;
    this.busy = true;
    this.error = '';
    this.refreshError = '';
    this.notice = '';
    request.pipe(timeout(30000), takeUntilDestroyed(this.destroyRef), finalize(() => { this.busy = false; })).subscribe({
      next: body => { this.busy = false; success(body); this.scheduleRefresh(parts | this.queuedRefreshParts); },
      error: failure => { this.handleError(failure); this.busy = false; this.scheduleRefresh(this.visibleRefreshParts); }
    });
  }

  private stopRefresh() {
    this.queuedRefreshParts = SessionRefresh.None;
    this.refreshSubscription?.unsubscribe();
    this.activeRefreshParts = SessionRefresh.None;
    this.refreshing = false;
  }
  private selectRequest(id: string) {
    this.selectedRequestId = id;
    try { sessionStorage.setItem(this.selectionKey, id); } catch { /* Selection also works without storage. */ }
    if ((this.route.snapshot.queryParamMap.get('requestId') ?? '') !== id)
      void this.router.navigate([], { relativeTo: this.route, queryParams: { requestId: id || null },
        queryParamsHandling: 'merge', replaceUrl: true });
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
