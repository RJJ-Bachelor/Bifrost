const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const ts = require('typescript');
const rx = require('rxjs');

function compile(filename, modules, globals = {}) {
  const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/app', filename), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, experimentalDecorators: true }
  }).outputText;
  const exports = {};
  vm.runInNewContext(source, { exports, ...globals, require: name => {
    if (!modules[name]) throw new Error('Unexpected dependency ' + name);
    return modules[name];
  } });
  return exports;
}

function fixture(role = 'teachers') {
  class HeimdallService {}
  class NotificationService {}
  class PushNotificationService {}
  class DestroyRef {}
  class ActivatedRoute {}
  class Router {}
  class HttpErrorResponse extends Error { constructor(status) { super(); this.status = status; } }
  const destroyed = new rx.Subject();
  const events = [];
  const state = { cleanup: () => Promise.resolve(), identity: { userId: 'current-user' } };
  const api = {
    get: url => { events.push(['get', url]); return rx.of(state.identity); },
    logout: () => events.push(['logout'])
  };
  const push = {
    setIdentity: async (...args) => { events.push(['identity', ...args]); },
    clearIdentity: async (...args) => { events.push(['clear', ...args]); },
    prepareIdentityChange: () => { events.push(['cleanup']); return state.cleanup(); }
  };
  const dependencies = new Map([
    [HeimdallService, api], [PushNotificationService, push],
    [NotificationService, { received: rx.NEVER, reconnected: rx.NEVER,
      connect: async (...args) => { events.push(['connect', ...args]); },
      stop: async (...args) => { events.push(['stop', ...args]); } }],
    [DestroyRef, { onDestroy: callback => destroyed.subscribe(callback) }],
    [ActivatedRoute, { snapshot: { data: { role }, queryParamMap: { get: () => null } }, paramMap: rx.NEVER, queryParamMap: rx.NEVER }],
    [Router, {}]
  ]);
  const { TestPageComponent } = compile('test-page.component.ts', {
    '@angular/common': { CommonModule: class {} },
    '@angular/common/http': { HttpErrorResponse },
    '@angular/core': { Component: () => target => target, DestroyRef, inject: token => dependencies.get(token) },
    '@angular/core/rxjs-interop': { takeUntilDestroyed: () => rx.takeUntil(destroyed) },
    '@angular/forms': { FormsModule: class {} },
    '@angular/router': { ActivatedRoute, Router, RouterLink: class {} },
    rxjs: rx,
    './notification.service': { NotificationService },
    './notifications/push/push-notification.service': { PushNotificationService },
    './notifications/push/push-notifications.component': { PushNotificationsComponent: class {} },
    './notification.model': compile('notification.model.ts', {}),
    './heimdall.service': { HeimdallService },
    './student/identity-transfer/student-identity-transfer.component': { StudentIdentityTransferComponent: class {} },
    './student/help-requests/student-help-requests.component': { StudentHelpRequestsComponent: class {} },
    './teacher/help-requests/teacher-help-requests.component': { TeacherHelpRequestsComponent: class {} }
  });
  const page = new TestPageComponent();
  page.identity = state.identity;
  page.refresh = () => {};
  return { page, state, events, HttpErrorResponse, destroy: () => { destroyed.next(); destroyed.complete(); } };
}

for (const role of ['students', 'teachers']) test(`successful ${role} authentication supplies the push owner`, t => {
  const { page, events, destroy } = fixture(role);
  t.after(destroy);
  page.loadIdentity();
  assert.deepEqual(events, [['get', role + '/me'], ['connect', role, 'current-user'], ['identity', role, 'current-user']]);
});

test('teacher logout waits for push cleanup before changing authentication', async t => {
  const { page, state, events, destroy } = fixture();
  t.after(destroy);
  let finishCleanup;
  state.cleanup = () => new Promise(resolve => { finishCleanup = resolve; });
  const pending = page.logout();
  assert.deepEqual(events, [['cleanup']]);
  assert.equal(page.busy, true);
  await page.logout();
  assert.deepEqual(events, [['cleanup']]);
  finishCleanup();
  await pending;
  assert.deepEqual(events, [['cleanup'], ['logout']]);
  assert.equal(page.busy, false);
});

test('unsafe push cleanup leaves the teacher logged in and permits retry', async t => {
  const { page, state, events, destroy } = fixture();
  t.after(destroy);
  state.cleanup = () => Promise.reject(new Error('Unsubscribe failed'));
  await page.logout();
  assert.deepEqual(events, [['cleanup']]);
  assert.equal(page.identity.userId, 'current-user');
  assert.equal(page.busy, false);
  assert.match(page.error, /Push-abonnementet/);
  state.cleanup = () => Promise.resolve();
  await page.logout();
  assert.deepEqual(events, [['cleanup'], ['cleanup'], ['logout']]);
});

for (const role of ['students', 'teachers']) test(`${role} authentication expiry only clears push ownership for that role`, t => {
  const { page, events, HttpErrorResponse, destroy } = fixture(role);
  t.after(destroy);
  page.handleError(new HttpErrorResponse(401));
  assert.deepEqual(events, [['stop', role], ['clear', role]]);
  assert.equal(page.identity, null);
});

test('ordinary request failures and page destruction preserve push registration', () => {
  const { page, events, HttpErrorResponse, destroy } = fixture();
  page.handleError(new HttpErrorResponse(500));
  destroy();
  assert.deepEqual(events, []);
});

test('login and logout use the current origin and preserve mobile return URLs', () => {
  class HttpClient {}
  const destinations = [];
  const { HeimdallService } = compile('heimdall.service.ts', {
    '@angular/common/http': { HttpClient },
    '@angular/core': { Injectable: () => target => target, inject: () => ({}) }
  }, { window: { location: { origin: 'https://bifrost.example', assign: value => destinations.push(value) } } });
  const api = new HeimdallService();
  api.login('/teachers/sessions/example');
  api.logout();
  assert.deepEqual(destinations, [
    '/account/login?returnUrl=' + encodeURIComponent('https://bifrost.example/teachers/sessions/example'),
    '/account/logout?returnUrl=' + encodeURIComponent('https://bifrost.example/teachers')
  ]);
});
