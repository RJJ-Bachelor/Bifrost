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

function fixture(role = 'students', helpType = 'Digital') {
  class HeimdallService {}
  class NotificationService {}
  class DestroyRef {}
  class ActivatedRoute {}
  class Router {}
  const destroyed = new rx.Subject();
  const requests = [];
  const scrolls = [];
  let refreshes = 0;
  const source = { id: 'source-id', sessionId: 'session-id', generation: 2, studentId: 'student-id',
    isFaq: false, helpType, queuedAt: '2026-10-24T08:00:00Z', faqAnswerId: 'faq-id', status: 'AwaitingConfirmation', messages: [
      { messageType: 'StudentQuestion', text: 'Mit eget spørgsmål' },
      { messageType: 'FaqQuestion', text: 'Det fælles spørgsmål' },
      { messageType: 'FaqAnswer', text: 'Underviserens fælles svar' }
    ] };
  const api = { patch: (url, body) => rx.defer(() => {
    requests.push({ url, body: { ...body } });
    return rx.of({ ...source, status: body.accept ? 'Completed' : 'Pending', faqAnswerId: body.accept ? 'faq-id' : null });
  }) };
  const dependencies = new Map([
    [HeimdallService, api],
    [NotificationService, { received: rx.NEVER, reconnected: rx.NEVER }],
    [DestroyRef, { onDestroy: callback => destroyed.subscribe(callback) }],
    [ActivatedRoute, { snapshot: { data: { role }, queryParamMap: { get: () => null } }, paramMap: rx.NEVER, queryParamMap: rx.NEVER }],
    [Router, {}]
  ]);
  const modules = {
    '@angular/common': { CommonModule: class {} },
    '@angular/common/http': { HttpErrorResponse: class extends Error {} },
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
  }, document: { getElementById: id => ({ scrollIntoView: () => scrolls.push(id) }) } }, { filename });
  const page = new exports.TestPageComponent();
  page.sessionId = source.sessionId;
  page.session = { id: source.sessionId, currentGeneration: source.generation };
  page.identity = { userId: source.studentId };
  page.requests = [source];
  page.conversation = source;
  page.selectedRequestId = source.id;
  page.refresh = () => refreshes++;
  return { page, source, requests, scrolls, refreshes: () => refreshes, destroy: () => { destroyed.next(); destroyed.complete(); } };
}

test('a student can find a ready FAQ response without an already selected conversation', t => {
  const { page, source, destroy } = fixture();
  t.after(destroy);
  page.conversation = null;
  page.requests = [source, { ...source, id: 'old', generation: 1 },
    { ...source, id: 'other-session', sessionId: 'other' }, { ...source, id: 'waiting', status: 'AwaitingFaq' },
    { ...source, id: 'foreign', studentId: 'other-student' }, { ...source, id: 'physical', helpType: 'Physical' }];
  assert.deepEqual(Array.from(page.faqResponsesAwaitingConfirmation, request => request.id), ['source-id', 'physical']);
  assert.equal(page.canRespondToFaq, false);
});

for (const helpType of ['Digital', 'Physical']) test(`the ${helpType} response panel uses the answer in the source conversation before the FAQ projection arrives`, t => {
  const { page, source, destroy } = fixture('students', helpType);
  t.after(destroy);
  page.faqs = [];
  source.messages.unshift({ messageType: 'FaqQuestion', text: 'Et gammelt spørgsmål' });
  source.messages.unshift({ messageType: 'FaqAnswer', text: 'Et gammelt svar' });
  assert.equal(page.canRespondToFaq, true);
  assert.equal(page.faqQuestion, 'Det fælles spørgsmål');
  assert.equal(page.faqAnswer, 'Underviserens fælles svar');
});

for (const helpType of ['Digital', 'Physical'])
for (const accept of [true, false]) test(`${accept ? 'accepting' : 'rejecting'} a ${helpType} FAQ updates the student's source request and removes the pending action`, t => {
  const { page, source, requests, refreshes, destroy } = fixture('students', helpType);
  t.after(destroy);
  // A response must target the displayed source conversation, never the shared FAQ id.
  page.selectedRequestId = 'faq-id';
  page.respondToFaq(accept);
  assert.deepEqual(requests, [{ url: 'students/requests/source-id/faq', body: { accept } }]);
  assert.equal(page.conversation.status, accept ? 'Completed' : 'Pending');
  assert.equal(page.conversation.helpType, helpType);
  assert.equal(page.conversation.queuedAt, source.queuedAt);
  assert.equal(page.conversation.faqAnswerId, accept ? 'faq-id' : null);
  if (!accept && helpType === 'Physical') assert.match(page.notice, /fysisk hjælp med din oprindelige køplads/);
  assert.equal(page.canRespondToFaq, false);
  assert.equal(page.busy, false);
  assert.equal(refreshes(), 1);
});

for (const helpType of ['Digital', 'Physical']) test(`no ${helpType} response is sent before the teacher answers, for an old generation, or for another owner`, t => {
  const { page, source, requests, destroy } = fixture('students', helpType);
  t.after(destroy);
  for (const change of [{ status: 'AwaitingFaq' }, { status: 'Completed' }, { status: 'Cancelled' },
    { generation: 1 }, { sessionId: 'other' }, { studentId: 'other-student' }, { faqAnswerId: null }]) {
    page.conversation = { ...source, ...change };
    page.respondToFaq(true);
    page.respondToFaq(false);
    assert.equal(page.canRespondToFaq, false);
  }
  page.conversation = source;
  page.identity = null;
  page.respondToFaq(true);
  page.respondToFaq(false);
  assert.equal(page.canRespondToFaq, false);
  assert.equal(page.faqResponsesAwaitingConfirmation.length, 0);
  page.identity = { userId: source.studentId };
  page.busy = true;
  page.respondToFaq(true);
  assert.equal(requests.length, 0);
});

for (const helpType of ['Digital', 'Physical']) test(`teachers cannot accept or reject a ${helpType} FAQ on behalf of the source student`, t => {
  const { page, requests, destroy } = fixture('teachers', helpType);
  t.after(destroy);
  page.respondToFaq(true);
  page.respondToFaq(false);
  assert.equal(page.canRespondToFaq, false);
  assert.equal(requests.length, 0);
});

test('the ready-answer action opens the source request and brings the conversation into view', t => {
  const { page, source, scrolls, destroy } = fixture();
  t.after(destroy);
  let opened;
  page.openRequest = id => opened = id;
  page.openFaqResponse(source.id);
  assert.equal(opened, source.id);
  assert.deepEqual(scrolls, ['conversation-title']);
});
