const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const ts = require('typescript');
const rx = require('rxjs');

const filename = path.join(__dirname, '../src/app/student/identity-transfer/student-identity-transfer.component.ts');
const compiled = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, experimentalDecorators: true }
}).outputText;

function fixture() {
  class HeimdallService {}
  class DestroyRef {}
  class HttpErrorResponse extends Error {
    constructor(status) { super(); this.status = typeof status === 'number' ? status : status?.status; }
  }
  const destroyed = new rx.Subject();
  const scheduler = new rx.VirtualTimeScheduler(undefined, 31000);
  const requests = [];
  const navigations = [];
  const copied = [];
  const storage = new Map([
    ['bifrost-students-session', 'student-session'],
    ['bifrost-students-session-one-request', 'old-request'],
    ['bifrost-students-session-two-request', 'another-old-request'],
    ['bifrost-teachers-session', 'teacher-session'],
    ['bifrost-theme', 'dark'],
    ['unrelated', 'keep']
  ]);
  const transfer = { code: 'ABCD2345', expiresAtUtc: '2026-11-12T14:10:00Z' };
  const state = {
    response: rx.of(transfer),
    storageUnavailable: false,
    clipboardAvailable: true,
    clipboardError: null
  };
  const browserStorage = {
    get length() {
      if (state.storageUnavailable) throw new Error('Storage is disabled');
      return storage.size;
    },
    key: index => Array.from(storage.keys())[index] ?? null,
    getItem: key => storage.get(key) ?? null,
    removeItem: key => {
      if (state.storageUnavailable) throw new Error('Storage is disabled');
      storage.delete(key);
    }
  };
  const api = { post: (url, body) => rx.defer(() => {
    requests.push({ url, body: body === null ? null : { ...body } });
    return state.response;
  }) };
  const dependencies = new Map([[HeimdallService, api], [DestroyRef, {}]]);
  const modules = {
    '@angular/common': { CommonModule: class {} },
    '@angular/common/http': { HttpErrorResponse },
    '@angular/core': {
      Component: () => target => target,
      Input: () => () => {},
      ChangeDetectionStrategy: { OnPush: 0 },
      DestroyRef,
      inject: token => dependencies.get(token)
    },
    '@angular/core/rxjs-interop': { takeUntilDestroyed: () => rx.takeUntil(destroyed) },
    '@angular/forms': { FormsModule: class {} },
    rxjs: {
      ...rx,
      timeout: options => rx.timeout(typeof options === 'number' ? { first: options, scheduler } : { ...options, scheduler })
    },
    'rxjs/operators': rx,
    '../../heimdall.service': { HeimdallService }
  };
  const exports = {};
  const navigator = {
    get clipboard() {
      return state.clipboardAvailable ? { writeText: async value => {
        copied.push(value);
        if (state.clipboardError) throw state.clipboardError;
      } } : undefined;
    }
  };
  vm.runInNewContext(compiled, {
    exports,
    require: name => {
      if (!modules[name]) throw new Error('Unexpected dependency ' + name);
      return modules[name];
    },
    sessionStorage: browserStorage,
    navigator,
    window: { sessionStorage: browserStorage, navigator, location: { replace: url => navigations.push(url) } }
  }, { filename });
  const component = new exports.StudentIdentityTransferComponent();
  component.userId = 'current-student';
  return {
    component, state, requests, navigations, copied, storage, transfer, scheduler, HttpErrorResponse,
    destroy: () => { destroyed.next(); destroyed.complete(); }
  };
}

test('creating a transfer code uses the cookie endpoint without sending a student identity', t => {
  const { component, requests, transfer, destroy } = fixture();
  t.after(destroy);
  component.error = 'Previous failure';

  component.createCode();

  assert.deepEqual(requests, [{ url: 'students/identity-transfer-codes', body: null }]);
  assert.equal(component.generatedCode, transfer);
  assert.equal(component.busy, false);
  assert.equal(component.error, '');
});

test('an in-flight command blocks repeated creation and redemption until the response arrives', t => {
  const { component, state, requests, transfer, destroy } = fixture();
  t.after(destroy);
  const pending = new rx.Subject();
  state.response = pending;
  component.redeemCode = transfer.code;

  component.createCode();
  component.createCode();
  component.redeemIdentity();

  assert.equal(requests.length, 1);
  assert.equal(component.busy, true);
  assert.equal(component.generatedCode, null);
  pending.next(transfer);
  pending.complete();
  assert.equal(component.generatedCode, transfer);
  assert.equal(component.busy, false);
});

test('redemption normalizes the code and clears only cached student selections after success', t => {
  const { component, state, requests, navigations, storage, destroy } = fixture();
  t.after(destroy);
  const pending = new rx.Subject();
  state.response = pending;
  const initialStorage = Array.from(storage.entries());
  component.redeemCode = ' abcd2345 ';

  component.redeemIdentity();
  component.redeemIdentity();
  component.createCode();

  assert.deepEqual(requests, [{ url: 'students/identity-transfer-codes/redeem', body: { code: 'ABCD2345' } }]);
  assert.equal(component.busy, true);
  assert.deepEqual(Array.from(storage.entries()), initialStorage);
  assert.deepEqual(navigations, []);
  pending.next(null);
  pending.complete();
  assert.equal(component.busy, false);
  assert.deepEqual(Array.from(storage.entries()), [
    ['bifrost-teachers-session', 'teacher-session'], ['bifrost-theme', 'dark'], ['unrelated', 'keep']
  ]);
  assert.deepEqual(navigations, ['/students']);
});

test('successful redemption still restarts the student page when browser storage is unavailable', t => {
  const { component, state, navigations, destroy } = fixture();
  t.after(destroy);
  component.redeemCode = 'ABCD2345';
  state.storageUnavailable = true;
  state.response = rx.of(null);

  component.redeemIdentity();

  assert.equal(component.busy, false);
  assert.deepEqual(navigations, ['/students']);
});

test('malformed or ambiguous codes never send a redemption request', t => {
  const { component, requests, navigations, destroy } = fixture();
  t.after(destroy);
  for (const code of ['', ' ', 'ABCD234', 'ABCD23456', 'ABCD0123', 'ABCDI234', 'ABCDO234', 'ABCD-234', 'ABCD 234']) {
    component.redeemCode = code;
    assert.equal(component.canRedeem, false, JSON.stringify(code));
    component.redeemIdentity();
  }
  assert.equal(requests.length, 0);
  assert.equal(component.busy, false);
  assert.deepEqual(navigations, []);
});

test('the redemption action accepts the shared eight-character alphabet regardless of casing', t => {
  const { component, destroy } = fixture();
  t.after(destroy);
  for (const code of ['ABCDEFGH', 'JKLMNPQR', 'STUVWXYZ', '23456789', ' abcd2345 ']) {
    component.redeemCode = code;
    assert.equal(component.canRedeem, true, JSON.stringify(code));
  }
});

for (const status of [404, 429, 500]) test(`a failed redemption (${status}) preserves the current identity's cache and permits retry`, t => {
  const { component, state, storage, navigations, HttpErrorResponse, destroy } = fixture();
  t.after(destroy);
  const initialStorage = Array.from(storage.entries());
  component.redeemCode = 'ABCD2345';
  state.response = rx.throwError(() => new HttpErrorResponse(status));

  component.redeemIdentity();

  assert.equal(component.redeemCode, 'ABCD2345');
  assert.equal(component.busy, false);
  assert.ok(component.error);
  if (status === 404) assert.match(component.error, /ugyldig|udløbet|brugt|findes/i);
  if (status === 429) assert.match(component.error, /1\s*minut|et minut/i);
  assert.deepEqual(Array.from(storage.entries()), initialStorage);
  assert.deepEqual(navigations, []);
  state.response = rx.of(null);
  component.redeemIdentity();
  assert.deepEqual(navigations, ['/students']);
});

test('a failed code creation ends loading and preserves the current browser identity', t => {
  const { component, state, storage, navigations, destroy } = fixture();
  t.after(destroy);
  const initialStorage = Array.from(storage.entries());
  state.response = rx.throwError(() => new Error('Connection failed'));

  component.createCode();

  assert.equal(component.generatedCode, null);
  assert.equal(component.busy, false);
  assert.ok(component.error);
  assert.deepEqual(Array.from(storage.entries()), initialStorage);
  assert.deepEqual(navigations, []);
});

for (const action of ['createCode', 'redeemIdentity']) test(`a stalled ${action} request stops loading after thirty seconds`, t => {
  const { component, state, scheduler, navigations, destroy } = fixture();
  t.after(destroy);
  state.response = rx.NEVER;
  component.redeemCode = 'ABCD2345';
  component[action]();
  scheduler.schedule(() => {
    assert.equal(component.busy, true);
    assert.equal(component.error, '');
  }, 29999);
  scheduler.schedule(() => {
    assert.equal(component.busy, false);
    assert.ok(component.error);
  }, 30001);

  scheduler.flush();

  assert.deepEqual(navigations, []);
});

for (const action of ['createCode', 'redeemIdentity']) test(`leaving the component cancels a pending ${action} request`, () => {
  const { component, state, transfer, navigations, storage, destroy } = fixture();
  const initialStorage = Array.from(storage.entries());
  const pending = new rx.Subject();
  state.response = pending;
  component.redeemCode = transfer.code;
  component[action]();
  assert.equal(component.busy, true);

  destroy();
  pending.next(action === 'createCode' ? transfer : null);
  pending.complete();

  assert.equal(component.busy, false);
  assert.equal(component.generatedCode, null);
  assert.deepEqual(Array.from(storage.entries()), initialStorage);
  assert.deepEqual(navigations, []);
});

test('copying a generated code puts only the code on the clipboard', async t => {
  const { component, transfer, copied, destroy } = fixture();
  t.after(destroy);
  component.generatedCode = transfer;

  await component.copyCode();

  assert.deepEqual(copied, [transfer.code]);
  assert.ok(component.notice);
});

for (const failure of ['unavailable', 'denied']) test(`clipboard ${failure} leaves the code available with a manual-copy notice`, async t => {
  const { component, state, transfer, navigations, destroy } = fixture();
  t.after(destroy);
  component.generatedCode = transfer;
  state.clipboardAvailable = failure !== 'unavailable';
  state.clipboardError = failure === 'denied' ? new Error('Permission denied') : null;

  await component.copyCode();

  assert.equal(component.generatedCode, transfer);
  assert.match(component.notice, /kopi|markér|marker/i);
  assert.deepEqual(navigations, []);
});
