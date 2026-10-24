const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const ts = require('typescript');
const rx = require('rxjs');

const filename = path.join(__dirname, '../src/app/test-page.component.ts');
const compiled = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, experimentalDecorators: true }
}).outputText;

function fixture(role = 'teachers') {
  class HeimdallService {}
  class NotificationService {}
  class DestroyRef {}
  class ActivatedRoute {}
  class Router {}
  class HttpErrorResponse extends Error { constructor(status) { super(); this.status = status; } }
  const destroyed = new rx.Subject();
  const posts = [];
  const reads = [];
  const routes = [];
  const storage = new Map();
  const detail = { id: 'archive-id', sessionId: 'session-id', generation: 2, faqs: [], conversations: [] };
  const state = { confirmed: true, confirmations: 0, refreshes: 0,
    archiveResult: rx.of({ archiveId: detail.id, sessionId: detail.sessionId, generation: 2, currentGeneration: 3 }),
    detailResult: () => rx.of(detail) };
  const api = {
    post: (url, body) => rx.defer(() => { posts.push({ url, body: { ...body } }); return state.archiveResult; }),
    get: url => rx.defer(() => { reads.push(url); return state.detailResult(url); })
  };
  const dependencies = new Map([
    [HeimdallService, api],
    [NotificationService, { received: rx.NEVER, reconnected: rx.NEVER }],
    [DestroyRef, { onDestroy: callback => destroyed.subscribe(callback) }],
    [ActivatedRoute, { snapshot: { data: { role }, queryParamMap: { get: () => null } }, paramMap: rx.NEVER, queryParamMap: rx.NEVER }],
    [Router, { navigate: (...args) => { routes.push(args); return Promise.resolve(true); } }]
  ]);
  const modules = {
    '@angular/common': { CommonModule: class {} },
    '@angular/common/http': { HttpErrorResponse },
    '@angular/core': { Component: () => target => target, DestroyRef, inject: token => dependencies.get(token) },
    '@angular/core/rxjs-interop': { takeUntilDestroyed: () => rx.takeUntil(destroyed) },
    '@angular/forms': { FormsModule: class {} },
    '@angular/router': { ActivatedRoute, Router, RouterLink: class {} },
    rxjs: { ...rx, interval: () => rx.NEVER },
    './notification.service': { NotificationService },
    './notification.model': { helpRequestId: value => value, notificationAffectsSession: () => true },
    './heimdall.service': { HeimdallService },
    './student/help-requests/student-help-requests.component': { StudentHelpRequestsComponent: class {} },
    './teacher/help-requests/teacher-help-requests.component': { TeacherHelpRequestsComponent: class {} }
  };
  const exports = {};
  vm.runInNewContext(compiled, { exports, require: name => {
    if (!modules[name]) throw new Error('Unexpected dependency ' + name);
    return modules[name];
  }, window: { confirm: () => { state.confirmations++; return state.confirmed; } },
  sessionStorage: { setItem: (key, value) => storage.set(key, value) } }, { filename });
  const page = new exports.TestPageComponent();
  page.sessionId = detail.sessionId;
  page.session = { id: detail.sessionId, currentGeneration: detail.generation };
  page.identity = { userId: 'identity-id' };
  page.selectedRequestId = 'live-request-id';
  page.conversation = { id: page.selectedRequestId };
  page.replyText = 'An old draft';
  page.refresh = () => state.refreshes++;
  return { page, state, posts, reads, detail, storage, HttpErrorResponse,
    destroy: () => { destroyed.next(); destroyed.complete(); } };
}

test('archiving uses the current generation, clears the live selection and opens the new archive', t => {
  const { page, state, posts, reads, detail, storage, destroy } = fixture();
  t.after(destroy);
  page.archiveSession();
  assert.deepEqual(posts, [{ url: 'teachers/sessions/session-id/archive', body: { expectedGeneration: 2 } }]);
  assert.equal(state.confirmations, 1);
  assert.equal(page.selectedRequestId, '');
  assert.equal(page.conversation, null);
  assert.equal(page.replyText, '');
  assert.equal(storage.get('bifrost-teachers-session-id-request'), '');
  assert.deepEqual(reads, ['teachers/sessions/session-id/archives/archive-id']);
  assert.equal(page.archive, detail);
  assert.equal(page.archiveLoading, false);
  assert.equal(state.refreshes, 1);
  assert.match(page.notice, /generation 3/);
});

test('cancelling, student access and a missing session cannot submit an archive command', t => {
  const teacher = fixture();
  const student = fixture('students');
  t.after(teacher.destroy);
  t.after(student.destroy);
  teacher.state.confirmed = false;
  teacher.page.archiveSession();
  teacher.page.session = null;
  teacher.page.archiveSession();
  student.page.archiveSession();
  assert.equal(teacher.posts.length, 0);
  assert.equal(student.posts.length, 0);
  assert.equal(teacher.state.confirmations, 1);
  assert.equal(student.state.confirmations, 0);
});

test('a second click cannot archive while the first command is in flight', t => {
  const { page, state, posts, destroy } = fixture();
  t.after(destroy);
  state.archiveResult = new rx.Subject();
  page.archiveSession();
  page.archiveSession();
  assert.equal(posts.length, 1);
  assert.equal(state.confirmations, 1);
  assert.equal(page.busy, true);
});

test('students load archive detail through their own endpoint and stale detail reads are cancelled', t => {
  const { page, state, reads, detail, destroy } = fixture('students');
  t.after(destroy);
  const first = new rx.Subject();
  const second = new rx.Subject();
  state.detailResult = url => url.endsWith('/first') ? first : second;
  page.openArchive('first');
  page.openArchive('second');
  first.next({ ...detail, id: 'first' });
  assert.equal(page.archive, null);
  assert.equal(page.archiveLoading, true);
  second.next({ ...detail, id: 'second' });
  second.complete();
  assert.deepEqual(reads, ['students/sessions/session-id/archives/first', 'students/sessions/session-id/archives/second']);
  assert.equal(page.archive.id, 'second');
  assert.equal(page.archiveLoading, false);
  page.closeArchive();
  assert.equal(page.archive, null);
  assert.equal(page.selectedArchiveId, '');
});

test('a failed archive detail read keeps its ID available for retry', t => {
  const { page, state, HttpErrorResponse, destroy } = fixture();
  t.after(destroy);
  state.detailResult = () => rx.throwError(() => new HttpErrorResponse(404));
  page.openArchive('missing');
  assert.equal(page.selectedArchiveId, 'missing');
  assert.equal(page.archiveLoading, false);
  assert.equal(page.archive, null);
  assert.match(page.archiveError, /findes ikke/);
});
