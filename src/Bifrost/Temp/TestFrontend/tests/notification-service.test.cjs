const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const ts = require('typescript');
const { Subject } = require('rxjs');

function compile(filename, dependencies = {}, globals = {}) {
  const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/app/', filename), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, experimentalDecorators: true }
  }).outputText;
  const exports = {};
  vm.runInNewContext(source, { exports, require: name => {
    if (!dependencies[name]) throw new Error('Unexpected dependency ' + name);
    return dependencies[name];
  }, setTimeout, clearTimeout, console, ...globals });
  return exports;
}

function fixture({ failStarts = 0, startGate, closeDuringStart = false } = {}) {
  const connections = [];
  const timers = new Map();
  let nextTimer = 0;
  class Connection {
    handlers = {};
    state = 'Disconnected';
    startCalls = 0;
    constructor(baseUrl) { this.baseUrl = baseUrl; }
    on(name, callback) { this.handlers[name] = callback; }
    onreconnecting(callback) { this.reconnecting = callback; }
    onreconnected(callback) { this.reconnected = callback; }
    onclose(callback) { this.closed = callback; }
    async start() {
      this.startCalls++;
      this.state = 'Connecting';
      if (startGate) await startGate;
      if (failStarts > 0) { failStarts--; this.state = 'Disconnected'; throw new Error('Unavailable'); }
      this.state = 'Connected';
      if (closeDuringStart) this.disconnect();
    }
    async stop() { this.state = 'Disconnected'; if (this.stopGate) await this.stopGate; this.closed?.(); }
    receive(notification) { this.handlers.NotificationReceived(notification); }
    beginReconnect() { this.state = 'Reconnecting'; this.reconnecting(); }
    finishReconnect() { this.state = 'Connected'; this.reconnected(); }
    disconnect() { this.state = 'Disconnected'; this.closed(); }
  }
  class Builder {
    withUrl(url) { this.url = url; return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() { const connection = new Connection(this.url); connections.push(connection); return connection; }
  }
  const signal = initial => {
    let value = initial;
    const read = () => value;
    read.set = next => value = next;
    read.update = update => value = update(value);
    return read;
  };
  const model = compile('notification.model.ts');
  const { NotificationService } = compile('notification.service.ts', {
    '@angular/core': { signal, computed: read => read, Injectable: () => target => target },
    '@microsoft/signalr': { HubConnectionBuilder: Builder, HubConnectionState: {
      Disconnected: 'Disconnected', Connecting: 'Connecting', Connected: 'Connected', Reconnecting: 'Reconnecting'
    }, LogLevel: { Warning: 3 } },
    rxjs: { Subject }, './notification.model': model
  }, {
    setTimeout: callback => { const id = ++nextTimer; timers.set(id, callback); return id; },
    clearTimeout: id => timers.delete(id),
    console: { warn() {} }
  });
  return { service: new NotificationService(), connections, timers,
    runRetry: async () => {
      const pending = [...timers];
      for (const [id, callback] of pending) { timers.delete(id); callback(); }
      await Promise.resolve();
      await Promise.resolve();
    }
  };
}
const notification = (userId, id = 'notice-1') => ({ notificationId: id, userId, message: 'FAQ answer',
  eventType: 'FaqAnswered', generation: 3, status: 'AwaitingConfirmation', recipientRole: 'teachers' });

test('typed notifications are queued once and routed by the actual connection identity', async () => {
  const { service, connections } = fixture();
  await service.connect('students', 'student-a');
  const incoming = notification('student-a');
  connections[0].receive(incoming);
  connections[0].receive(incoming);
  connections[0].receive(notification('other-student'));
  connections[0].receive(notification('student-a', 'notice-2'));
  assert.equal(service.count(), 2);
  assert.equal(service.current().eventType, 'FaqAnswered');
  assert.equal(service.current().generation, 3);
  assert.equal(service.current().recipientRole, 'students');
  service.dismiss();
  assert.equal(service.current().notificationId, 'notice-2');
  await service.stop();
});

test('changing the user within the same role closes old state and ignores delayed deliveries', async () => {
  const { service, connections } = fixture();
  await service.connect('students', 'student-a');
  connections[0].receive(notification('student-a'));
  await service.connect('students', 'student-a');
  assert.equal(connections.length, 1);
  await service.connect('students', 'student-b');
  assert.equal(connections.length, 2);
  assert.equal(service.count(), 0);
  connections[0].receive(notification('student-a', 'late'));
  assert.equal(service.count(), 0);
  connections[1].receive(notification('student-b'));
  assert.equal(service.current().userId, 'student-b');
  await service.stop('teachers');
  assert.equal(service.count(), 1);
  await service.stop();
});

test('a slower previous role switch cannot replace the latest connection', async () => {
  const { service, connections } = fixture();
  await service.connect('students', 'student-a');
  let finishStop;
  connections[0].stopGate = new Promise(resolve => finishStop = resolve);
  const previous = service.connect('teachers', 'teacher');
  await service.connect('students', 'student-b');
  finishStop();
  await previous;
  assert.equal(connections.length, 2);
  assert.ok(connections[1].baseUrl.endsWith('/students/notifications'));
  connections[1].receive(notification('student-b'));
  assert.equal(service.current().userId, 'student-b');
  let reconnected = 0;
  const subscription = service.reconnected.subscribe(() => reconnected++);
  connections[0].reconnected();
  assert.equal(reconnected, 0);
  connections[1].reconnected();
  assert.equal(reconnected, 1);
  subscription.unsubscribe();
  await service.stop();
});

test('realtime-only changes refresh subscribers and show a modal once per notification', async () => {
  const { service, connections } = fixture();
  const received = [];
  const subscription = service.received.subscribe(message => received.push(message));
  await service.connect('students', 'student-a');
  const update = { ...notification('student-a'), eventType: 'SessionChanged', deliveryType: 'RealtimeOnly' };
  connections[0].receive(update);
  connections[0].receive(update);
  assert.equal(received.length, 1);
  assert.equal(received[0].recipientRole, 'students');
  assert.equal(service.count(), 1);
  assert.equal(service.current().notificationId, update.notificationId);
  assert.equal(service.title(), 'Sessionen er opdateret');
  assert.equal(service.current().deliveryType, 'RealtimeOnly');
  connections[0].receive({ ...notification('student-a', 'answer'), deliveryType: 'RealtimeOrPush' });
  assert.equal(received.length, 2);
  assert.equal(service.count(), 2);
  service.dismiss();
  assert.equal(service.current().notificationId, 'answer');
  subscription.unsubscribe();
  await service.stop();
});

test('initial connection and reconnect each request a fresh snapshot', async () => {
  const { service, connections } = fixture();
  let refreshes = 0;
  const subscription = service.reconnected.subscribe(() => refreshes++);
  await service.connect('students', 'student-a');
  assert.equal(refreshes, 1);
  connections[0].reconnected();
  assert.equal(refreshes, 2);
  subscription.unsubscribe();
  await service.stop();
});

test('connection indicator is red until startup completes and green only while connected', async () => {
  let finishStart;
  const startGate = new Promise(resolve => finishStart = resolve);
  const { service, connections } = fixture({ startGate });
  assert.equal(service.connected(), false);
  const connecting = service.connect('students', 'student-a');
  await new Promise(setImmediate);
  await service.connect('students', 'student-a');
  assert.equal(connections.length, 1);
  assert.equal(connections[0].startCalls, 1);
  assert.equal(service.connected(), false);
  finishStart();
  await connecting;
  assert.equal(service.connected(), true);
  connections[0].beginReconnect();
  assert.equal(service.connected(), false);
  connections[0].finishReconnect();
  assert.equal(service.connected(), true);
  await service.stop();
  assert.equal(service.connected(), false);
});

test('failed initial startup retries once and refreshes the snapshot after recovery', async () => {
  const { service, connections, timers, runRetry } = fixture({ failStarts: 1 });
  let snapshots = 0;
  const subscription = service.reconnected.subscribe(() => snapshots++);
  await service.connect('students', 'student-a');
  assert.equal(service.connected(), false);
  assert.equal(snapshots, 0);
  assert.equal(timers.size, 1);
  await service.connect('students', 'student-a');
  assert.equal(connections[0].startCalls, 1);
  await runRetry();
  assert.equal(connections[0].startCalls, 2);
  assert.equal(timers.size, 0);
  assert.equal(service.connected(), true);
  assert.equal(snapshots, 1);
  subscription.unsubscribe();
  await service.stop();
});

test('exhausted automatic reconnect turns the indicator red and a later retry restores updates', async () => {
  const { service, connections, timers, runRetry } = fixture();
  const received = [];
  const subscription = service.received.subscribe(message => received.push(message));
  await service.connect('students', 'student-a');
  connections[0].beginReconnect();
  assert.equal(service.connected(), false);
  assert.equal(timers.size, 0);
  connections[0].disconnect();
  assert.equal(service.connected(), false);
  assert.equal(timers.size, 1);
  await runRetry();
  assert.equal(service.connected(), true);
  connections[0].receive(notification('student-a'));
  assert.equal(received.length, 1);
  connections[0].disconnect();
  await service.stop();
  assert.equal(service.connected(), false);
  assert.equal(timers.size, 0);
  await runRetry();
  assert.equal(connections[0].startCalls, 2);
  subscription.unsubscribe();
});

test('delayed callbacks from a replaced identity cannot change the current connection indicator', async () => {
  const { service, connections, timers } = fixture();
  await service.connect('students', 'student-a');
  await service.connect('teachers', 'teacher-b');
  assert.equal(service.connected(), true);
  connections[0].beginReconnect();
  connections[0].disconnect();
  assert.equal(service.connected(), true);
  assert.equal(timers.size, 0);
  connections[1].beginReconnect();
  connections[0].finishReconnect();
  assert.equal(service.connected(), false);
  connections[1].finishReconnect();
  assert.equal(service.connected(), true);
  await service.stop();
  connections[1].finishReconnect();
  assert.equal(service.connected(), false);
});
test('a connection closed before startup resolves stays red and awaits retry instead of refreshing', async () => {
  const { service, connections, timers } = fixture({ closeDuringStart: true });
  let snapshots = 0;
  const subscription = service.reconnected.subscribe(() => snapshots++);
  await service.connect('students', 'student-a');
  assert.equal(connections[0].state, 'Disconnected');
  assert.equal(service.connected(), false);
  assert.equal(snapshots, 0);
  assert.equal(timers.size, 1);
  subscription.unsubscribe();
  await service.stop();
  assert.equal(timers.size, 0);
});
