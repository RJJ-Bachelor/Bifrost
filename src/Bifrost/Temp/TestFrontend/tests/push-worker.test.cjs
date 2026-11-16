const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

const publicPath = path.join(__dirname, '../public');

function worker({ windows = [], scope = 'https://bifrost.example/' } = {}) {
  const handlers = {};
  const shown = [];
  const opened = [];
  let skipped = 0;
  let claimed = 0;
  vm.runInNewContext(fs.readFileSync(path.join(publicPath, 'push-sw.js'), 'utf8'), {
    URL,
    self: {
      addEventListener: (name, handler) => { handlers[name] = handler; },
      skipWaiting: async () => { skipped++; },
      registration: {
        scope,
        showNotification: async (title, options) => { shown.push({ title, options }); }
      },
      clients: {
        claim: async () => { claimed++; },
        matchAll: async options => {
          assert.equal(options.type, 'window');
          assert.equal(options.includeUncontrolled, true);
          return windows;
        },
        openWindow: async url => { opened.push(url); }
      }
    }
  });
  return { handlers, shown, opened,
    lifecycle: () => ({ skipped, claimed }),
    async dispatch(name, event = {}) {
      let pending;
      handlers[name]({ ...event, waitUntil: promise => { pending = promise; } });
      assert.equal(typeof pending?.then, 'function', 'worker keeps the event alive');
      await pending;
    }
  };
}

test('Gna push envelope displays its title, preview and duplicate tag', async () => {
  const subject = worker();
  const data = { notificationId: 'notice-1', sessionId: 'session-1', eventType: 'FaqAnswered' };
  await subject.dispatch('push', { data: { json: () => ({ notification: {
    title: 'Bifrost', body: 'Underviseren har svaret', tag: 'notice-1', data
  } }) } });
  assert.equal(subject.shown.length, 1);
  const { title, options } = subject.shown[0];
  assert.equal(title, 'Bifrost');
  assert.equal(options.body, 'Underviseren har svaret');
  assert.equal(options.tag, 'notice-1');
  assert.equal(options.data, data);
  assert.equal(options.icon, '/icons/icon-192.png');
  assert.equal(options.badge, '/icons/badge-96.png');
});

test('missing, malformed and invalid push payloads still show a visible notification', async () => {
  for (const event of [
    {},
    { data: { json: () => { throw new SyntaxError('Invalid JSON'); } } },
    { data: { json: () => null } },
    { data: { json: () => ({}) } },
    { data: { json: () => ({ notification: { title: 12, body: '   ', tag: {} } }) } }
  ]) {
    const subject = worker();
    await subject.dispatch('push', event);
    assert.equal(subject.shown.length, 1);
    assert.equal(subject.shown[0].title, 'Bifrost');
    assert.ok(subject.shown[0].options.body.length > 0);
    assert.equal(subject.shown[0].options.tag, undefined);
  }
});

test('notification click closes the notification and focuses an existing same-origin app', async () => {
  const focused = [];
  let closed = 0;
  const subject = worker({ windows: [
    { url: 'https://outside.example/students', focus: async () => focused.push('outside') },
    { url: 'https://bifrost.example/teachers/sessions/1', focus: async () => focused.push('app') }
  ] });
  await subject.dispatch('notificationclick', { notification: {
    close: () => { closed++; }, data: { url: 'https://untrusted.example', recipientRole: 'students' }
  } });
  assert.equal(closed, 1);
  assert.deepEqual(focused, ['app']);
  assert.deepEqual(subject.opened, []);
});

test('notification click opens only the app root when no in-scope window is available', async () => {
  const subject = worker({ scope: 'https://bifrost.example/app/', windows: [
    { url: 'https://outside.example/app/', focus: async () => assert.fail('external origin') },
    { url: 'https://bifrost.example/elsewhere', focus: async () => assert.fail('outside scope') }
  ] });
  await subject.dispatch('notificationclick', { notification: {
    close() {}, data: { url: 'https://untrusted.example', sessionId: '../account/logout' }
  } });
  assert.deepEqual(subject.opened, ['https://bifrost.example/']);
});

test('a window closing during a click falls back to opening the app', async () => {
  const subject = worker({ windows: [
    { url: 'https://bifrost.example/students', focus: async () => { throw new Error('Window closed'); } }
  ] });
  await subject.dispatch('notificationclick', { notification: { close() {} } });
  assert.deepEqual(subject.opened, ['https://bifrost.example/']);
});

test('worker updates claim the app without intercepting or caching authenticated requests', async () => {
  const subject = worker();
  await subject.dispatch('install');
  await subject.dispatch('activate');
  assert.deepEqual(subject.lifecycle(), { skipped: 1, claimed: 1 });
  assert.deepEqual(Object.keys(subject.handlers).sort(), ['activate', 'install', 'notificationclick', 'push']);
});

test('installable manifest and notification icons reference real PNGs at the declared sizes', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(publicPath, 'manifest.webmanifest'), 'utf8'));
  assert.equal(manifest.display, 'standalone');
  assert.equal(manifest.start_url, '/');
  assert.equal(manifest.scope, '/');
  const icons = [...manifest.icons,
    { src: '/icons/apple-touch-icon.png', sizes: '180x180' },
    { src: '/icons/badge-96.png', sizes: '96x96' }];
  for (const icon of icons) {
    const png = fs.readFileSync(path.join(publicPath, icon.src));
    assert.equal(png.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
    assert.equal(`${png.readUInt32BE(16)}x${png.readUInt32BE(20)}`, icon.sizes);
  }
});
