const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');
const ts = require('typescript');

const filename = path.join(__dirname, '../src/app/notification.model.ts');
const compiled = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText;
const helpers = {};
vm.runInNewContext(compiled, { exports: helpers }, { filename });
const sessionId = '11111111-1111-1111-1111-111111111111';
const requestId = '22222222-2222-2222-2222-222222222222';
const notification = { sessionId, helpRequestId: requestId, generation: 2, recipientRole: 'students', eventType: 'FaqAnswered' };

test('FAQ notifications have distinct teacher/question and student/answer titles', () => {
  assert.equal(helpers.notificationTitle(notification), 'Svar på dit fælles FAQ-spørgsmål');
  assert.equal(helpers.notificationTitle({ ...notification, eventType: 'FaqCreated' }), 'Nyt fælles FAQ-spørgsmål');
  assert.equal(helpers.notificationTitle({ ...notification, eventType: 'HelpRequestAnswered' }), 'Underviseren har svaret');
});

test('legacy and future event types retain a usable fallback title', () => {
  for (const value of [null, {}, { eventType: 'FutureEvent' }])
    assert.equal(helpers.notificationTitle(value), 'Ny notifikation');
});

test('notification links use the connection role and the source request id', () => {
  assert.equal(helpers.notificationTarget(notification), `/students/sessions/${sessionId}?requestId=${requestId}`);
  assert.equal(helpers.notificationTarget({ ...notification, recipientRole: 'teachers', eventType: 'FaqCreated' }),
    `/teachers/sessions/${sessionId}?requestId=${requestId}`);
  assert.equal(helpers.notificationTarget({ ...notification, helpRequestId: null }), `/students/sessions/${sessionId}`);
});

test('missing identity context or malformed ids cannot create a navigation target', () => {
  assert.equal(helpers.notificationTarget({ ...notification, recipientRole: undefined }), null);
  assert.equal(helpers.notificationTarget({ ...notification, sessionId: '../teachers' }), null);
  assert.equal(helpers.notificationTarget({ ...notification, helpRequestId: 'bad?requestId=other' }), `/students/sessions/${sessionId}`);
  assert.equal(helpers.helpRequestId('00000000-0000-0000-0000-000000000000'), null);
});

test('only matching session generation and recipient role trigger a conversation refresh', () => {
  assert.equal(helpers.notificationAffectsSession(notification, 'students', sessionId, 2), true);
  assert.equal(helpers.notificationAffectsSession(notification, 'teachers', sessionId, 2), false);
  assert.equal(helpers.notificationAffectsSession(notification, 'students', requestId, 2), false);
  assert.equal(helpers.notificationAffectsSession(notification, 'students', sessionId, 3), false);
  assert.equal(helpers.notificationAffectsSession(notification, 'students', '', 2), true);
  assert.equal(helpers.notificationAffectsSession({ sessionId: null }, 'students', sessionId, 2), true);
});
