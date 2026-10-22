const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const ts = require('typescript');
const { Subject } = require('rxjs');

function compile(filename, dependencies = {}) {
  const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/app/', filename), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, experimentalDecorators: true }
  }).outputText;
  const exports = {};
  vm.runInNewContext(source, { exports, require: name => {
    if (!dependencies[name]) throw new Error('Unexpected dependency ' + name);
    return dependencies[name];
  }, setTimeout, clearTimeout, console });
  return exports;
}

function fixture() {
  const connections = [];
  class Connection {
    handlers = {};
    state = 'Disconnected';
    constructor(baseUrl) { this.baseUrl = baseUrl; }
    on(name, callback) { this.handlers[name] = callback; }
    onreconnected(callback) { this.reconnected = callback; }
    onclose(callback) { this.closed = callback; }
    async start() { this.state = 'Connected'; }
    async stop() { this.state = 'Disconnected'; if (this.stopGate) await this.stopGate; this.closed?.(); }
    receive(notification) { this.handlers.NotificationReceived(notification); }
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
    '@microsoft/signalr': { HubConnectionBuilder: Builder, HubConnectionState: { Disconnected: 'Disconnected' }, LogLevel: { Warning: 3 } },
    rxjs: { Subject }, './notification.model': model
  });
  return { service: new NotificationService(), connections };
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
