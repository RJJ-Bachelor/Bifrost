const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const ts = require('typescript');
const rx = require('rxjs');
const { TestScheduler } = require('rxjs/testing');

function compile(filename, modules = {}) {
  const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/app/', filename), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, experimentalDecorators: true }
  }).outputText;
  const exports = {};
  vm.runInNewContext(source, { exports, require: name => {
    if (!modules[name]) throw new Error('Unexpected dependency ' + name);
    return modules[name];
  }, window: { confirm: () => true } });
  return exports;
}

const sessionId = '11111111-1111-1111-1111-111111111111';
const otherSessionId = '22222222-2222-2222-2222-222222222222';
const requestId = '33333333-3333-3333-3333-333333333333';
const update = { recipientRole: 'teachers', sessionId, eventType: 'SessionChanged', deliveryType: 'RealtimeOnly', generation: 3, change: 'Created' };

function fixture(role = 'teachers') {
  class HeimdallService {}
  class NotificationService {}
  class DestroyRef {}
  class ActivatedRoute {}
  class Router {}
  class HttpErrorResponse extends Error { constructor(status) { super(); this.status = status; } }
  const scheduler = new TestScheduler(assert.deepEqual);
  const destroyed = new rx.Subject();
  const received = new rx.Subject();
  const reconnected = new rx.Subject();
  const reads = [];
  const session = { id: sessionId, currentGeneration: 3 };
  const conversation = { id: requestId, sessionId, generation: 3, messages: [] };
  const state = { session, sessions: [session], gate: null, requests: [], queueRequests: [], queueActive: [], conversation, archives: [], archive: null, archiveResult: null, conversations: {}, command: null };
  const api = { patch: () => state.command, post: () => state.command, get: url => rx.defer(() => {
    reads.push(url);
    if (url === role + '/sessions') return rx.of(state.sessions);
    if (url === role + '/sessions/' + sessionId)
      return state.session ? rx.of(state.session) : rx.throwError(() => new HttpErrorResponse(404));
    if (url.endsWith('/queue')) return state.gate ?? rx.of({ sessionId, generation: state.session?.currentGeneration, requests: state.queueRequests, activeRequests: state.queueActive, waitingCount: 2 });
    if (url.includes('/requests/' + requestId)) return rx.of(state.conversation);
    if (url.includes('/requests/')) return rx.of(state.conversations[url.split('/').at(-1)] ?? null);
    if (url.includes('/requests?')) return rx.of(state.requests);
    if (url.endsWith('/archives')) return rx.of(state.archives);
    if (url.includes('/archives/')) return state.archiveResult ?? rx.of(state.archive);
    return rx.of([]);
  }) };
  const dependencies = new Map([
    [HeimdallService, api],
    [NotificationService, { received, reconnected }],
    [DestroyRef, { onDestroy: callback => destroyed.subscribe(callback) }],
    [ActivatedRoute, { snapshot: { data: { role }, queryParamMap: { get: () => null } }, paramMap: rx.NEVER, queryParamMap: rx.NEVER }],
    [Router, { navigate: () => Promise.resolve(true) }]
  ]);
  const { TestPageComponent } = compile('test-page.component.ts', {
    '@angular/common': { CommonModule: class {} },
    '@angular/common/http': { HttpErrorResponse },
    '@angular/core': { Component: () => target => target, DestroyRef, inject: token => dependencies.get(token) },
    '@angular/core/rxjs-interop': { takeUntilDestroyed: () => rx.takeUntil(destroyed) },
    '@angular/forms': { FormsModule: class {} },
    '@angular/router': { ActivatedRoute, Router, RouterLink: class {} },
    rxjs: { ...rx, auditTime: duration => rx.auditTime(duration, scheduler),
      // RxJS identifies dictionary arguments by their realm's Object prototype.
      forkJoin: sources => rx.forkJoin({ ...sources }),
      interval: () => { throw new Error('The session page must not poll'); } },
    './notification.service': { NotificationService },
    './notification.model': compile('notification.model.ts'),
    './heimdall.service': { HeimdallService },
    './student/help-requests/student-help-requests.component': { StudentHelpRequestsComponent: class {} },
    './teacher/help-requests/teacher-help-requests.component': { TeacherHelpRequestsComponent: class {} }
  });
  const page = new TestPageComponent();
  page.identity = { userId: 'identity-id' };
  page.sessionId = sessionId;
  page.session = session;
  page.selectedRequestId = requestId;
  page.conversation = conversation;
  page.replyText = 'Keep my draft';
  return { page, state, reads, received, reconnected, scheduler, conversation,
    flush: () => scheduler.flush(),
    destroy: () => { destroyed.next(); destroyed.complete(); } };
}

for (const role of ['teachers', 'students']) test(`${role} fetch only the queue for another student's queue change`, t => {
  const { page, reads, received, flush, conversation, destroy } = fixture(role);
  t.after(destroy);
  received.next({ ...update, recipientRole: role });
  flush();
  assert.deepEqual(reads, [role + '/sessions/' + sessionId + '/queue']);
  assert.equal(page.conversation, conversation);
  assert.equal(page.replyText, 'Keep my draft');
  assert.equal((page.teacherQueue ?? page.studentQueue).waitingCount, 2);
});

test('events for other sessions, old generations and other roles do not fetch hidden data', t => {
  const { page, reads, received, flush, conversation, destroy } = fixture();
  t.after(destroy);
  received.next({ ...update, sessionId: otherSessionId });
  received.next({ ...update, eventType: 'HelpRequestAnswered', generation: 2 });
  received.next({ ...update, recipientRole: 'students' });
  flush();
  assert.deepEqual(reads, []);
  assert.equal(page.conversation, conversation);
});

test('a burst keeps the current queue refresh when its last event belongs to another session', t => {
  const { reads, received, scheduler, flush, destroy } = fixture();
  t.after(destroy);
  received.next(update);
  scheduler.schedule(() => received.next({ ...update, sessionId: otherSessionId }), 50);
  flush();
  assert.deepEqual(reads, ['teachers/sessions/' + sessionId + '/queue']);
});

for (const change of ['Cleared', 'Archived']) test(`${change} refreshes all visible resources and advances the generation`, t => {
  const { page, state, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  state.session = { ...state.session, currentGeneration: 4 };
  received.next({ ...update, change, generation: 4 });
  flush();
  assert.equal(page.session.currentGeneration, 4);
  assert.equal(page.teacherQueue.generation, 4);
  assert.ok(reads.some(url => url.endsWith('/archives')));
  assert.ok(!reads.includes('teachers/sessions'));
});

test('deleting the current session updates its unavailable state without fetching a hidden list', t => {
  const { page, state, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  state.session = null;
  received.next({ ...update, change: 'Deleted' });
  flush();
  assert.equal(page.session, null);
  assert.equal(page.sessionUnavailable, true);
  assert.ok(!reads.includes('teachers/sessions'));
});

test('reconnection reloads visible resources and destruction stops event-driven reads', t => {
  const { page, reads, received, reconnected, flush, destroy } = fixture();
  t.after(destroy);
  reconnected.next();
  flush();
  assert.ok(reads.some(url => url.endsWith('/queue')));
  assert.ok(reads.some(url => url.endsWith('/archives')));
  assert.ok(!reads.includes('teachers/sessions'));
  assert.equal(page.refreshing, false);
  reads.length = 0;
  destroy();
  received.next(update);
  reconnected.next();
  flush();
  assert.deepEqual(reads, []);
});

test('resource changes in flight are merged into one further refresh without losing FAQ data', t => {
  const { page, state, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  const gate = new rx.Subject();
  state.gate = gate;
  received.next(update);
  flush();
  received.next({ ...update, change: 'FaqCreated' });
  flush();
  received.next({ ...update, sessionId: otherSessionId });
  flush();
  assert.equal(reads.length, 1);
  state.gate = null;
  gate.next({ sessionId, generation: 3, requests: [], activeRequests: [], waitingCount: 1 });
  gate.complete();
  assert.equal(reads.filter(url => url.endsWith('/queue')).length, 2);
  assert.equal(reads.filter(url => url.endsWith('/faqs')).length, 1);
  assert.equal(page.refreshing, false);
});

test('a continuous stream of notifications refreshes snapshots before the session becomes quiet', t => {
  const { reads, received, scheduler, flush, destroy } = fixture();
  t.after(destroy);
  for (let time = 0; time <= 1000; time += 100)
    scheduler.schedule(() => received.next(update), time);
  scheduler.schedule(() => {
    assert.ok(reads.some(url => url.endsWith('/queue')), 'The queue must refresh during sustained traffic');
  }, 500);
  scheduler.maxFrames = 1200;
  flush();
  const snapshots = reads.filter(url => url.endsWith('/queue')).length;
  assert.ok(snapshots >= 5);
  assert.ok(snapshots <= 6, 'Consecutive events should still be batched');
});

for (const change of ['Reserved', 'Completed', 'FaqAccepted', 'FaqCancelled', 'ArchivedFaqAnswered'])
test(`teacher session cards do not reload for ${change}, which leaves waitingCount unchanged`, t => {
  const { page, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  page.sessionId = '';
  received.next({ ...update, change });
  flush();
  assert.deepEqual(reads, []);
});

test('teacher session cards reload only their list for a changed waiting count', t => {
  const { page, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  page.sessionId = '';
  received.next(update);
  flush();
  assert.deepEqual(reads, ['teachers/sessions']);
});

test('student session cards ignore queue changes and reload after session lifecycle changes', t => {
  const { page, reads, received, flush, destroy } = fixture('students');
  t.after(destroy);
  page.sessionId = '';
  received.next({ ...update, recipientRole: 'students' });
  flush();
  assert.deepEqual(reads, []);
  received.next({ ...update, recipientRole: 'students', change: 'Archived', generation: 4 });
  flush();
  assert.deepEqual(reads, ['students/sessions']);
});

test('a selected teacher conversation is reused from its queue with no separate GET', t => {
  const { page, state, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  state.queueRequests = [{ ...state.conversation, status: 'Pending', assignedTeacherId: 'teacher-id' }];
  received.next({ ...update, change: 'Reserved', helpRequestId: requestId });
  flush();
  assert.deepEqual(reads, ['teachers/sessions/' + sessionId + '/queue']);
  assert.equal(page.conversation.assignedTeacherId, 'teacher-id');
  assert.equal(page.replyText, 'Keep my draft');
});

test('a selected request removed from the teacher queue is fetched once after its answer', t => {
  const { page, state, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  state.conversation = { ...state.conversation, status: 'Completed' };
  received.next({ ...update, change: 'Answered', helpRequestId: requestId });
  flush();
  assert.deepEqual(reads, ['teachers/sessions/' + sessionId + '/queue', 'teachers/requests/' + requestId]);
  assert.equal(page.conversation.status, 'Completed');
});

test('reserving the student request fetches only its selected conversation', t => {
  const { page, state, reads, received, flush, destroy } = fixture('students');
  t.after(destroy);
  state.conversation = { ...state.conversation, status: 'Pending', assignedTeacherId: 'teacher-id' };
  received.next({ ...update, recipientRole: 'students', change: 'Reserved', helpRequestId: requestId });
  flush();
  assert.deepEqual(reads, ['students/requests/' + requestId]);
  assert.equal(page.conversation.assignedTeacherId, 'teacher-id');
});

for (const change of ['FaqAccepted', 'FaqCancelled']) test(`${change} refreshes the owner's open request reference and summaries`, t => {
  const { reads, received, flush, destroy } = fixture('students');
  t.after(destroy);
  received.next({ ...update, recipientRole: 'students', change, helpRequestId: requestId });
  flush();
  assert.deepEqual(reads.sort(), ['students/sessions/' + sessionId + '/queue',
    'students/requests?sessionId=' + sessionId, 'students/requests/' + requestId].sort());
});

for (const change of ['FaqCreated', 'FaqLinked']) test(`${change} refreshes the teacher queue and FAQ projection together`, t => {
  const { reads, received, flush, destroy } = fixture();
  t.after(destroy);
  received.next({ ...update, change });
  flush();
  assert.deepEqual(reads.sort(), ['teachers/sessions/' + sessionId + '/queue', 'teachers/sessions/' + sessionId + '/faqs'].sort());
});

test('a late FAQ answer refreshes only the archive list and the archive currently open', t => {
  const { page, state, reads, received, flush, destroy } = fixture('students');
  t.after(destroy);
  page.selectedArchiveId = 'archive-id';
  state.archive = { id: 'archive-id', faqs: [{ question: 'Old question', answer: 'Late answer' }] };
  received.next({ ...update, recipientRole: 'students', change: 'ArchivedFaqAnswered' });
  flush();
  assert.deepEqual(reads.sort(), ['students/sessions/' + sessionId + '/archives', 'students/sessions/' + sessionId + '/archives/archive-id'].sort());
  assert.equal(page.archive.faqs[0].answer, 'Late answer');
});

for (const change of ['QueueChanged', 'FutureChange']) test(`legacy or unknown ${change} notifications keep a full visible refresh`, t => {
  const { reads, received, flush, destroy } = fixture();
  t.after(destroy);
  received.next({ ...update, change });
  flush();
  assert.ok(reads.includes('teachers/sessions/' + sessionId));
  assert.ok(reads.some(url => url.endsWith('/queue')));
  assert.ok(reads.some(url => url.endsWith('/faqs')));
  assert.ok(reads.some(url => url.endsWith('/archives')));
  assert.ok(!reads.includes('teachers/sessions'));
});
test('a canonical FAQ answer also reloads a selected source that is waiting outside the queue', t => {
  const { page, state, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  page.conversation = { ...state.conversation, status: 'AwaitingFaq' };
  state.conversation = { ...page.conversation, status: 'AwaitingConfirmation' };
  received.next({ ...update, change: 'FaqAnswered' });
  flush();
  assert.deepEqual(reads.sort(), ['teachers/sessions/' + sessionId + '/queue',
    'teachers/sessions/' + sessionId + '/faqs', 'teachers/requests/' + requestId].sort());
  assert.equal(page.conversation.status, 'AwaitingConfirmation');
});

test('a successful own answer and its immediate echo share one targeted refresh batch', t => {
  const { page, state, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  page.conversation = { ...state.conversation, status: 'Pending', helpType: 'Digital', assignedTeacherId: page.identity.userId };
  const answered = { ...page.conversation, status: 'Completed', messages: [{ messageType: 'TeacherAnswer', text: 'Answer' }] };
  state.command = rx.of(answered);
  state.conversation = answered;
  page.sendMessage();
  assert.equal(page.conversation, answered);
  assert.deepEqual(reads, []);
  received.next({ ...update, change: 'Answered', helpRequestId: requestId });
  flush();
  assert.equal(reads.filter(url => url.endsWith('/queue')).length, 1);
  assert.equal(reads.filter(url => url.includes('/requests/')).length, 1);
  assert.ok(!reads.some(url => url.endsWith('/archives') || url.endsWith('/faqs')));
});

test('own FAQ acceptance updates its local summary and only fetches its queue without an echo', t => {
  const { page, state, reads, flush, destroy } = fixture('students');
  t.after(destroy);
  page.conversation = { ...state.conversation, studentId: page.identity.userId, status: 'AwaitingConfirmation', faqAnswerId: 'faq-id' };
  page.requests = [page.conversation];
  state.command = rx.of({ ...page.conversation, status: 'Completed' });
  page.respondToFaq(true);
  assert.equal(page.conversation.status, 'Completed');
  assert.equal(page.requests[0].status, 'Completed');
  flush();
  assert.deepEqual(reads, ['students/sessions/' + sessionId + '/queue']);
});

test('opening a different conversation while a queue read is in flight preserves the new selection', t => {
  const { page, state, received, flush, destroy } = fixture();
  t.after(destroy);
  const gate = new rx.Subject();
  state.gate = gate;
  received.next(update);
  flush();
  const nextId = '44444444-4444-4444-4444-444444444444';
  state.conversations[nextId] = { ...state.conversation, id: nextId };
  page.openRequest(nextId);
  state.gate = null;
  gate.next({ sessionId, generation: 3, requests: [state.conversation], activeRequests: [], waitingCount: 1 });
  gate.complete();
  assert.equal(page.selectedRequestId, nextId);
  assert.equal(page.conversation.id, nextId);
});
for (const change of ['Created', 'ArchivedFaqAnswered']) test(`a newer generation ${change} event refreshes metadata even before its archive event arrives`, t => {
  const { page, state, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  state.session = { ...state.session, currentGeneration: 4 };
  received.next({ ...update, change, generation: 4 });
  flush();
  assert.ok(reads.includes('teachers/sessions/' + sessionId));
  assert.equal(page.session.currentGeneration, 4);
  assert.equal(page.teacherQueue.generation, 4);
});
test('an archive change while its detail is loading cancels the old read and fetches the new answer', t => {
  const { page, state, reads, received, flush, destroy } = fixture('students');
  t.after(destroy);
  const stale = new rx.Subject();
  state.archiveResult = stale;
  page.openArchive('archive-id');
  assert.equal(page.archiveLoading, true);
  const updated = { id: 'archive-id', faqs: [{ answer: 'Late answer' }] };
  state.archiveResult = rx.of(updated);
  received.next({ ...update, recipientRole: 'students', change: 'ArchivedFaqAnswered' });
  flush();
  stale.next({ id: 'archive-id', faqs: [] });
  stale.complete();
  assert.equal(page.archiveLoading, false);
  assert.equal(page.archive, updated);
  assert.equal(reads.filter(url => url.endsWith('/archives/archive-id')).length, 2);
  assert.equal(reads.filter(url => url.endsWith('/archives')).length, 1);
});

test('archiving and its notification fetch the new archive detail only once', t => {
  const { page, state, reads, received, flush, destroy } = fixture();
  t.after(destroy);
  state.command = rx.of({ archiveId: 'archive-id', generation: 3, currentGeneration: 4 });
  state.archive = { id: 'archive-id', generation: 3, faqs: [], conversations: [] };
  state.session = { ...state.session, currentGeneration: 4 };
  page.archiveSession();
  assert.deepEqual(reads, []);
  assert.equal(page.selectedArchiveId, 'archive-id');
  received.next({ ...update, change: 'Archived', generation: 4 });
  flush();
  assert.equal(reads.filter(url => url.endsWith('/archives/archive-id')).length, 1);
  assert.equal(reads.filter(url => url.endsWith('/archives')).length, 1);
  assert.equal(page.archive, state.archive);
  assert.equal(page.session.currentGeneration, 4);
});
