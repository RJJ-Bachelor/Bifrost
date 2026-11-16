const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const ts = require('typescript');
const rx = require('rxjs');

const filename = path.join(__dirname, '../src/app/notifications/push/push-notification.service.ts');
const compiled = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, experimentalDecorators: true }
}).outputText;
const bytes = Uint8Array.from({ length: 65 }, (_, index) => index === 0 ? 4 : index);
const publicKey = Buffer.from(bytes).toString('base64url');
const ownerKey = 'bifrost-push-owner';
const flush = () => new Promise(setImmediate);

function fixture(options = {}) {
  class HttpClient {}
  const requests = [];
  const events = {};
  const storage = new Map();
  const state = {
    permission: 'default', permissionCalls: 0, subscribeCalls: 0, unsubscribeCalls: 0,
    registrations: [], current: null, gesture: false, storageBlocked: false,
    fail: {}, response: {}, key: publicKey, ...options
  };
  const owner = (role, userId, endpoint) => storage.set(ownerKey, JSON.stringify({ role, userId, endpoint }));
  const subscription = (endpoint = 'https://push.example/device', key = bytes) => ({
    endpoint,
    options: { applicationServerKey: key.buffer.slice(key.byteOffset, key.byteOffset + key.byteLength) },
    toJSON: () => ({ endpoint, keys: { p256dh: 'device-key', auth: 'device-secret' } }),
    unsubscribe: async () => {
      state.unsubscribeCalls++;
      if (state.unsubscribeFailure) throw new Error('Browser unavailable');
      if (state.unsubscribeFalse) return false;
      if (state.current?.endpoint === endpoint) state.current = null;
      return true;
    }
  });
  const registration = {
    active: {},
    pushManager: {
      getSubscription: async () => state.current,
      subscribe: async options => {
        state.subscribeCalls++;
        state.subscribeOptions = options;
        if (state.subscribeFailure) throw new Error('Subscribe failed');
        const value = subscription('https://push.example/device-' + state.subscribeCalls);
        if (state.subscribeGate) await state.subscribeGate;
        state.current = value;
        return value;
      }
    }
  };
  const notification = {
    get permission() { return state.permission; },
    requestPermission: () => {
      state.permissionCalls++;
      assert.equal(state.gesture, true, 'Permission must be requested synchronously from the button gesture');
      state.permission = state.requestedPermission ?? 'granted';
      return Promise.resolve(state.permission);
    }
  };
  const http = { request: (method, url, options) => rx.defer(() => {
    requests.push({ method, url, ...options });
    if (state.response[method]) return state.response[method];
    if (state.fail[method]) return rx.throwError(() => state.fail[method]);
    return rx.of(method === 'GET' ? { publicKey: state.key } : undefined);
  }) };
  const signal = initial => {
    let value = initial;
    const read = () => value;
    read.set = next => { value = next; };
    return read;
  };
  const navigator = {
    onLine: options.online !== false,
    serviceWorker: {
      register: async (url, options) => {
        state.registrations.push({ url, ...options });
        if (state.registrationFailure) throw new Error('Worker unavailable');
        return registration;
      },
      ready: Promise.resolve(registration)
    }
  };
  const window = {
    isSecureContext: options.secure !== false,
    PushManager: class {}, Notification: notification,
    addEventListener: (name, callback) => { events[name] = callback; }
  };
  if (options.unsupported) delete window.PushManager;
  const modules = {
    '@angular/core': { Injectable: () => target => target, inject: token => token === HttpClient ? http : undefined, signal },
    '@angular/common/http': { HttpClient },
    rxjs: rx
  };
  const exports = {};
  vm.runInNewContext(compiled, {
    exports, require: name => modules[name], window, navigator, Notification: notification,
    localStorage: {
      getItem: key => {
        if (state.storageBlocked) throw new Error('Storage unavailable');
        return storage.get(key) ?? null;
      },
      setItem: (key, value) => {
        if (state.storageBlocked) throw new Error('Storage unavailable');
        storage.set(key, value);
      },
      removeItem: key => {
        if (state.storageBlocked) throw new Error('Storage unavailable');
        storage.delete(key);
      }
    },
    atob: value => Buffer.from(value, 'base64').toString('binary'),
    Uint8Array, setTimeout, clearTimeout
  }, { filename });
  const service = new exports.PushNotificationService();
  const enable = () => {
    state.gesture = true;
    const result = service.enable();
    state.gesture = false;
    return result;
  };
  return { service, state, requests, storage, events, owner, subscription, enable };
}

for (const role of ['students', 'teachers']) test(`${role} register only from the button, using cookie-authenticated endpoints and browser keys`, async () => {
  const { service, state, requests, storage, enable } = fixture();
  await service.setIdentity(role, role + '-user');
  assert.equal(service.ready(), true);
  assert.equal(state.permissionCalls, 0);
  assert.equal(state.subscribeCalls, 0);
  assert.deepEqual(state.registrations, [{ url: '/push-sw.js', scope: '/' }]);

  const enabling = enable();
  assert.equal(state.permissionCalls, 1);
  await enabling;

  assert.equal(service.enabled(), true);
  assert.equal(service.busy(), false);
  assert.equal(state.subscribeOptions.userVisibleOnly, true);
  assert.deepEqual(Array.from(state.subscribeOptions.applicationServerKey), Array.from(bytes));
  assert.deepEqual(requests.map(request => [request.method, request.url]), [
    ['GET', `/api/${role}/notifications/push/public-key`],
    ['PUT', `/api/${role}/notifications/push/subscriptions`]
  ]);
  assert.ok(requests.every(request => request.withCredentials === true));
  assert.deepEqual(JSON.parse(JSON.stringify(requests[1].body)), state.current.toJSON());
  assert.deepEqual(JSON.parse(storage.get(ownerKey)), { role, userId: role + '-user', endpoint: state.current.endpoint });
});

test('no identity, insecure origins and unsupported browsers never request permission', async () => {
  for (const options of [{}, { secure: false }, { unsupported: true }]) {
    const { service, state, enable } = fixture(options);
    await enable();
    if (Object.keys(options).length) await service.setIdentity('students', 'student');
    await enable();
    assert.equal(state.permissionCalls, 0);
    assert.equal(state.subscribeCalls, 0);
    if (Object.keys(options).length) assert.equal(state.registrations.length, 0);
  }
});

test('the same owner restores an existing subscription without prompting or resubscribing', async () => {
  const { service, state, owner, subscription, requests } = fixture({ permission: 'granted' });
  state.current = subscription();
  owner('students', 'student', state.current.endpoint);
  await service.setIdentity('students', 'student');
  assert.equal(service.enabled(), true);
  assert.equal(state.permissionCalls, 0);
  assert.equal(state.subscribeCalls, 0);
  assert.equal(requests.at(-1).method, 'PUT');
});

test('denied permission leaves push disabled and gives actionable feedback', async () => {
  const { service, state, enable } = fixture({ requestedPermission: 'denied' });
  await service.setIdentity('students', 'student');
  await enable();
  assert.equal(service.enabled(), false);
  assert.match(service.error(), /indstillinger/);
  assert.equal(state.subscribeCalls, 0);
});

for (const failure of ['subscribe', 'PUT', 'storage']) test(`${failure} failure does not leave a newly created device subscription active`, async () => {
  const { service, state, enable } = fixture();
  await service.setIdentity('students', 'student');
  if (failure === 'subscribe') state.subscribeFailure = true;
  if (failure === 'PUT') state.fail.PUT = { status: 500 };
  if (failure === 'storage') state.storageBlocked = true;
  await enable();
  assert.equal(service.enabled(), false);
  assert.equal(service.busy(), false);
  assert.ok(service.error());
  assert.equal(state.current, null);
});

for (const status of [0, 401, 503]) test(`public key failure ${status} is visible without prompting`, async () => {
  const { service, state } = fixture({ fail: { GET: { status } } });
  await service.setIdentity('teachers', 'teacher');
  assert.equal(service.ready(), false);
  assert.equal(service.busy(), false);
  assert.equal(state.permissionCalls, 0);
  assert.ok(service.error());
  if (status === 503) assert.match(service.error(), /serveren/);
  if (status === 401) assert.match(service.error(), /identitet/);
});

test('invalid public keys and unavailable workers prevent activation', async () => {
  for (const options of [{ key: 'invalid' }, { registrationFailure: true }]) {
    const { service, enable, state } = fixture(options);
    await service.setIdentity('students', 'student');
    await enable();
    assert.equal(service.ready(), false);
    assert.ok(service.error());
    assert.equal(state.permissionCalls, 0);
  }
});

test('disable removes browser and server subscriptions with a JSON DELETE body', async () => {
  const { service, state, requests, storage, enable } = fixture();
  await service.setIdentity('teachers', 'teacher');
  await enable();
  const endpoint = state.current.endpoint;
  await service.disable();
  assert.equal(service.enabled(), false);
  assert.equal(state.current, null);
  assert.equal(storage.has(ownerKey), false);
  assert.deepEqual(JSON.parse(JSON.stringify(requests.at(-1))), {
    method: 'DELETE', url: '/api/teachers/notifications/push/subscriptions', body: { endpoint }, withCredentials: true
  });
});

test('local unsubscribe makes logout safe even when the server is offline', async () => {
  const { service, state, storage, enable } = fixture();
  await service.setIdentity('students', 'student');
  await enable();
  state.fail.DELETE = { status: 0 };
  await service.prepareIdentityChange();
  assert.equal(state.current, null);
  assert.equal(storage.has(ownerKey), false);
  assert.equal(service.enabled(), false);
});

test('identity changes fail closed if neither browser nor server can stop delivery', async () => {
  const { service, state, enable } = fixture();
  await service.setIdentity('students', 'student');
  await enable();
  state.unsubscribeFailure = true;
  state.fail.DELETE = { status: 0 };
  await assert.rejects(service.prepareIdentityChange(), /sikkert/);
  assert.ok(state.current);
  assert.ok(service.error());
});

test('a recorded owner and unavailable worker require successful authenticated deletion before identity change', async () => {
  const { service, state, owner } = fixture({ registrationFailure: true });
  owner('students', 'student', 'https://push.example/previous');
  await service.setIdentity('students', 'student');
  state.fail.DELETE = { status: 0 };
  await assert.rejects(service.prepareIdentityChange(), /sikkert/);
});

test('the retry button can recover failed worker registration without requesting permission', async () => {
  const { service, state } = fixture({ registrationFailure: true });
  await service.setIdentity('students', 'student');
  assert.equal(service.ready(), false);
  state.registrationFailure = false;
  await service.retry();
  assert.equal(service.ready(), true);
  assert.equal(state.registrations.length, 2);
  assert.equal(state.permissionCalls, 0);
});

test('401 cleanup is local and does not retry expired authentication', async () => {
  const { service, state, requests, enable } = fixture();
  await service.setIdentity('teachers', 'teacher');
  await enable();
  const count = requests.length;
  await service.clearIdentity('students');
  assert.equal(service.hasIdentity(), true);
  await service.clearIdentity('teachers');
  assert.equal(state.current, null);
  assert.equal(service.hasIdentity(), false);
  assert.equal(requests.length, count);
});

test('switching roles removes the old endpoint and never automatically enables the new owner', async () => {
  const { service, state, requests, enable } = fixture();
  await service.setIdentity('students', 'student');
  await enable();
  await service.setIdentity('teachers', 'teacher');
  assert.equal(state.current, null);
  assert.equal(service.enabled(), false);
  assert.equal(service.ready(), true);
  assert.ok(requests.some(request => request.method === 'DELETE' && request.url.includes('/students/')));
  assert.equal(requests.filter(request => request.method === 'PUT').length, 1);
});

test('a reloaded different identity invalidates the previous endpoint without using its new cookie to delete the old owner', async () => {
  const { service, state, requests, owner, subscription } = fixture({ permission: 'granted' });
  state.current = subscription();
  owner('students', 'previous-user', state.current.endpoint);
  await service.setIdentity('students', 'new-user');
  assert.equal(state.current, null);
  assert.equal(service.enabled(), false);
  assert.ok(!requests.some(request => request.method === 'DELETE'));
  assert.ok(!requests.some(request => request.method === 'PUT'));
});

test('rotating the server key removes the previous subscription and requires a new button action', async () => {
  const { service, state, owner, subscription } = fixture({ permission: 'granted' });
  const oldKey = Uint8Array.from(bytes, byte => byte === 4 ? 4 : 100);
  state.current = subscription('https://push.example/old-key', oldKey);
  owner('students', 'student', state.current.endpoint);
  await service.setIdentity('students', 'student');
  assert.equal(state.current, null);
  assert.equal(service.ready(), true);
  assert.equal(service.enabled(), false);
  assert.match(service.notice(), /nøglen/);
  assert.equal(state.subscribeCalls, 0);
});

test('a delayed key response cannot overwrite a later identity', async () => {
  const { service, state, requests, enable } = fixture();
  const oldKey = new rx.Subject();
  state.response.GET = oldKey;
  const oldIdentity = service.setIdentity('students', 'student');
  await flush();
  const newIdentity = service.setIdentity('teachers', 'teacher');
  delete state.response.GET;
  oldKey.next({ publicKey });
  oldKey.complete();
  await Promise.all([oldIdentity, newIdentity]);
  await enable();
  assert.equal(service.enabled(), true);
  assert.ok(requests.filter(request => request.method === 'PUT').every(request => request.url.includes('/teachers/')));
});

test('a delayed old-owner PUT is cleaned up before the later identity becomes ready', async () => {
  const { service, state, requests, owner, subscription } = fixture({ permission: 'granted' });
  state.current = subscription();
  owner('students', 'student', state.current.endpoint);
  const pending = new rx.Subject();
  state.response.PUT = pending;
  const oldIdentity = service.setIdentity('students', 'student');
  await flush();
  const newIdentity = service.setIdentity('teachers', 'teacher');
  pending.next(undefined);
  pending.complete();
  await Promise.all([oldIdentity, newIdentity]);
  assert.equal(service.ready(), true);
  assert.equal(service.enabled(), false);
  assert.equal(state.current, null);
  assert.ok(requests.some(request => request.method === 'DELETE'));
});

test('a subscription completed after identity replacement is removed before a new owner can register', async () => {
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  const { service, state, requests, enable } = fixture({ subscribeGate: gate });
  await service.setIdentity('students', 'student');
  const enabling = enable();
  await flush();
  const switching = service.setIdentity('teachers', 'teacher');
  release();
  await Promise.all([enabling, switching]);
  assert.equal(state.current, null);
  assert.equal(service.enabled(), false);
  assert.ok(!requests.some(request => request.method === 'PUT'));
});

test('cross-tab owner changes invalidate stale UI and require a fresh identity check', async () => {
  const { service, state, events, owner, enable } = fixture();
  await service.setIdentity('students', 'student');
  await enable();
  owner('teachers', 'teacher', state.current.endpoint);
  events.storage({ key: ownerKey });
  assert.equal(service.enabled(), false);
  assert.equal(service.ready(), false);
  await enable();
  assert.equal(state.subscribeCalls, 1);
  await service.retry();
  assert.equal(state.current, null);
  assert.equal(service.ready(), true);
});
