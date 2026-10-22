const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createRequire } = require('node:module');
const { test } = require('node:test');
const yaml = require('js-yaml');
const Bru = require('@usebruno/js/src/bru');
const BrunoRequest = require('@usebruno/js/src/bruno-request');
const BrunoResponse = require('@usebruno/js/src/bruno-response');

const collectionPath = path.resolve(__dirname, '..');
const requireFromCollection = createRequire(path.join(collectionPath, 'package.json'));
const read = name => yaml.load(fs.readFileSync(path.join(collectionPath, name), 'utf8'));
const seed = read('Development/SeedFAQdemo.yml');
const publish = read('MimirService/PublishGeneralization.yml');
const localVariables = Object.fromEntries(read('environments/Local.yml').variables.map(({ name, value }) => [name, value]));
const seedData = {
  session: {
    id: '11111111-1111-1111-1111-111111111111',
    code: 'STUDENT1', teacherCode: 'TEACHER1', currentGeneration: 1
  },
  students: [
    { helpRequestId: '22222222-2222-2222-2222-222222222222', cookieHeader: 'bifrost-anonymous-user=first' },
    { helpRequestId: '33333333-3333-3333-3333-333333333333', cookieHeader: 'bifrost-anonymous-user=second' }
  ],
  generalization: {
    generalizationId: '44444444-4444-4444-4444-444444444444',
    sessionId: '11111111-1111-1111-1111-111111111111', generation: 1,
    question: 'Hvordan sikrer jeg, at min l\u00f8kke stopper?',
    helpRequestIds: ['22222222-2222-2222-2222-222222222222', '33333333-3333-3333-3333-333333333333']
  }
};

function createBru(runtimeVariables = {}, envVariables = {}) {
  return new Bru({ runtime: 'nodevm', collectionPath, runtimeVariables, envVariables,
    collectionVariables: { gatewayUrl: 'http://localhost:6004' } });
}

function run(document, type, context) {
  const script = document.runtime.scripts.find(script => script.type === type);
  return vm.runInNewContext(script.code, { require: requireFromCollection, ...context }, { filename: document.info.name + '/' + type });
}

function response(status, data) {
  return new BrunoResponse({ status, data, headers: { 'content-type': 'application/json' } });
}

function request(body = publish.http.body.data) {
  return new BrunoRequest({
    method: 'POST', url: publish.http.url,
    headers: { 'Content-Type': 'application/json' }, data: body
  });
}

function seedSuccessfully(bru, body = seedData) {
  run(seed, 'before-request', { bru });
  run(seed, 'after-response', { bru, res: response(201, body) });
}

for (const activeEnvironment of [true, false]) {
  test(`seed then publish resolves valid ids with${activeEnvironment ? '' : 'out'} an active environment`, () => {
    const runtime = {};
    const environment = activeEnvironment ? { ...localVariables } : {};
    seedSuccessfully(createBru(runtime, environment));
    // Runtime values survive even when no selected environment carries script updates forward.
    const nextRequest = createBru(runtime, activeEnvironment ? environment : {});
    const req = request();

    run(publish, 'before-request', { bru: nextRequest, req });

    assert.deepEqual(JSON.parse(req.getBody({ raw: true })), seedData.generalization);
    assert.equal(runtime.seedReady, true);
    assert.equal(runtime.firstStudentCookie, seedData.students[0].cookieHeader);
    assert.equal(runtime.secondStudentCookie, seedData.students[1].cookieHeader);
    assert.equal(environment.generalizationId, seedData.generalization.generalizationId);
    assert.equal(runtime.sessionId, seedData.session.id);
    assert.equal(runtime.sessionGeneration, String(seedData.session.currentGeneration));
    for (const [file, expectedId] of [
      ['Development/Getteacherqueue.yml', seedData.session.id],
      ['Development/Getfirststudentrequest.yml', seedData.students[0].helpRequestId],
      ['Development/Getsecondstudentrequest.yml', seedData.students[1].helpRequestId]
    ]) {
      const url = nextRequest.interpolate(read(file).http.url);
      assert.ok(url.includes(expectedId));
      assert.ok(!url.includes('{{'));
    }
  });
}

test('an unseeded request leaves its id unresolved and is stopped before sending', () => {
  const bru = createBru();
  const unresolved = bru.interpolate(publish.http.body.data);
  const idLine = unresolved.split('\n')[1];

  assert.match(idLine, /"generalizationId": "\{\{faqDemoGeneralizationId\}\}"/);
  assert.throws(() => run(publish, 'before-request', { bru, req: request() }), /not resolved JSON/);
});

test('an unresolved generalizationId is rejected before sending, even if the other variables exist', () => {
  const runtime = {};
  seedSuccessfully(createBru(runtime));
  delete runtime.faqDemoGeneralizationId;
  const req = request();

  assert.throws(() => run(publish, 'before-request', { bru: createBru(runtime), req }), /generalizationId.*not a GUID/);
  assert.equal(req.getBody({ raw: true }), publish.http.body.data);
});

test('invalid source ids and unresolved numeric generation cannot reach the API', () => {
  for (const changes of [{ faqDemoSecondHelpRequestId: '{{missingId}}' }, { faqDemoGeneration: '{{missingGeneration}}' }]) {
    const runtime = {};
    seedSuccessfully(createBru(runtime));
    Object.assign(runtime, changes);
    const req = request();

    assert.throws(() => run(publish, 'before-request', { bru: createBru(runtime), req }), /helpRequestIds|not resolved JSON/);
    assert.equal(req.getBody({ raw: true }), publish.http.body.data);
  }
});

test('manual JSON can omit the optional id and reuse the id returned by Mimir', () => {
  const runtime = {};
  const bru = createBru(runtime);
  seedSuccessfully(bru);
  const manual = { ...seedData.generalization, question: 'A custom question' };
  delete manual.generalizationId;
  const req = request(JSON.stringify(manual));

  run(publish, 'before-request', { bru, req });

  assert.deepEqual(JSON.parse(req.getBody({ raw: true })), manual);
  const generated = '55555555-5555-5555-5555-555555555555';
  run(publish, 'after-response', { bru, res: response(202, { generalizationId: generated }) });
  const retry = request();
  run(publish, 'before-request', { bru: createBru(runtime), req: retry });
  assert.equal(JSON.parse(retry.getBody({ raw: true })).generalizationId, generated);
});

test('seed can read a JSON response string and does not overwrite ids on a failed response', () => {
  const runtime = {};
  const environment = { ...localVariables };
  const bru = createBru(runtime, environment);
  seedSuccessfully(bru, JSON.stringify(seedData));
  const previousEnvironment = { ...environment };

  run(seed, 'before-request', { bru });
  run(seed, 'after-response', { bru, res: response(500, {}) });

  assert.deepEqual(environment, previousEnvironment);
  assert.equal(runtime.faqDemoGeneralizationId, seedData.generalization.generalizationId);
  assert.equal(runtime.seedReady, false);
});

test('an incomplete seed response cannot mark the demo ready or overwrite existing ids', () => {
  const runtime = {};
  const bru = createBru(runtime);
  seedSuccessfully(bru);
  const invalid = { ...seedData, generalization: {} };

  run(seed, 'before-request', { bru });
  assert.throws(() => run(seed, 'after-response', { bru, res: response(201, invalid) }), /seed response is missing valid/);

  assert.equal(runtime.seedReady, false);
  assert.equal(runtime.faqDemoGeneralizationId, seedData.generalization.generalizationId);
});

test('demo reserves and answers the FAQ, then responds with each source student identity', async () => {
  const runtime = {};
  const bru = createBru(runtime);
  seedSuccessfully(bru);
  const take = read('Development/TakeNextFaq.yml');
  const answer = read('Development/AnswerFaq.yml');
  const requestFor = document => new BrunoRequest({
    method: document.http.method, url: bru.interpolate(document.http.url),
    headers: { 'Content-Type': 'application/json' }, data: document.http.body?.data
  });

  assert.throws(() => run(answer, 'before-request', { bru, req: requestFor(answer) }), /Take next FAQ/);
  run(take, 'before-request', { bru });
  run(take, 'after-response', { bru, res: response(200, { id: seedData.generalization.generalizationId, isFaq: true }) });
  const answerRequest = requestFor(answer);
  run(answer, 'before-request', { bru, req: answerRequest });
  assert.equal(answerRequest.getUrl(), `http://localhost:6004/api/teachers/requests/${seedData.generalization.generalizationId}/messages`);
  assert.ok(answerRequest.getBody().text.length > 0);

  const cookies = [
    { key: 'bifrost-teacher', value: 'teacher-cookie' },
    { key: 'bifrost-anonymous-user', value: 'previous-student' }
  ];
  bru.cookies = { jar: () => ({
    getCookies: async () => [...cookies],
    deleteCookie: async (_url, key) => cookies.splice(cookies.findIndex(cookie => cookie.key === key), 1),
    setCookie: async (_url, cookie) => cookies.push(cookie)
  }) };
  for (const [file, studentIndex, accept] of [
    ['Development/AcceptFirstStudentFaq.yml', 0, true],
    ['Development/RejectSecondStudentFaq.yml', 1, false]
  ]) {
    const document = read(file);
    const req = requestFor(document);
    const script = document.runtime.scripts.find(script => script.type === 'before-request');
    await vm.runInNewContext('(async () => {\n' + script.code + '\n})()', { bru, req, require: requireFromCollection });

    assert.equal(req.getUrl(), `http://localhost:6004/api/students/requests/${seedData.students[studentIndex].helpRequestId}/faq`);
    assert.equal(req.getBody().accept, accept);
    assert.equal(req.getHeader('Cookie'), seedData.students[studentIndex].cookieHeader);
    assert.equal(cookies.find(cookie => cookie.key === 'bifrost-anonymous-user').value, studentIndex === 0 ? 'first' : 'second');
    assert.equal(cookies.find(cookie => cookie.key === 'bifrost-teacher').value, 'teacher-cookie');
  }
  assert.equal(runtime.helpRequestId, seedData.students[0].helpRequestId);
});

test('demo cannot answer an ordinary reserved request and a fresh seed clears the reservation', () => {
  const runtime = {};
  const bru = createBru(runtime);
  seedSuccessfully(bru);
  const take = read('Development/TakeNextFaq.yml');
  const answer = read('Development/AnswerFaq.yml');
  run(take, 'before-request', { bru });
  run(take, 'after-response', { bru, res: response(200, { id: seedData.students[0].helpRequestId, isFaq: false }) });

  assert.throws(() => run(answer, 'before-request', { bru }), /Take next FAQ/);

  seedSuccessfully(bru);
  assert.equal(runtime.faqDemoTakenRequestId, null);
});

const own = read('MimirService/GeneralizeOwnHelpRequests.yml');
const collection = read('opencollection.yml');
const ownPayload = {
  sessionId: '66666666-6666-6666-6666-666666666666', generation: 3,
  helpRequestIds: ['77777777-7777-7777-7777-777777777777', '88888888-8888-8888-8888-888888888888']
};
function ownRequest(body = own.http.body.data) {
  return new BrunoRequest({ method: own.http.method, url: own.http.url,
    headers: { 'Content-Type': 'application/json' }, data: body });
}
function captureOwnResponse(bru, req, status, data) {
  const script = collection.request.scripts.find(script => script.type === 'after-response');
  return vm.runInNewContext(script.code, { require: requireFromCollection, bru, req, res: response(status, data) });
}

for (const activeEnvironment of [true, false]) {
  test(`own HelpRequests publish without seed using standard session variables with${activeEnvironment ? '' : 'out'} Local`, () => {
    const runtime = { sessionId: ownPayload.sessionId, sessionGeneration: String(ownPayload.generation),
      studentHelpRequestId: ownPayload.helpRequestIds[0], faqDemoGeneralizationId: seedData.generalization.generalizationId };
    const environment = activeEnvironment ? { ...localVariables } : {};
    const bru = createBru(runtime, environment);
    const edited = own.http.body.data
      .replace('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', ownPayload.helpRequestIds[0])
      .replace('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', ownPayload.helpRequestIds[1]);
    const req = ownRequest(edited);

    run(own, 'before-request', { bru, req });

    assert.deepEqual(req.getBody(), ownPayload);
    assert.equal(runtime.seedReady, undefined);
    assert.equal(req.getBody().question, undefined);
    assert.equal(bru.interpolate(req.getUrl()), 'http://localhost:6004/api/teachers/development/generalizations/from-help-requests');
    const generated = '99999999-9999-9999-9999-999999999999';
    captureOwnResponse(bru, req, 202, { generalizationId: generated });
    assert.equal(runtime.generalizationId, generated);
    assert.equal(environment.generalizationId, generated);
    assert.equal(runtime.faqDemoGeneralizationId, seedData.generalization.generalizationId);
    assert.equal(runtime.studentHelpRequestId, ownPayload.helpRequestIds[0]);
  });
}

test('own HelpRequests stop unresolved session values and unchanged example ids before sending', () => {
  assert.throws(() => run(own, 'before-request', { bru: createBru(), req: ownRequest() }), /sessionId.*generation/);
  const bru = createBru({ sessionId: ownPayload.sessionId, sessionGeneration: '3', seedReady: false });
  assert.throws(() => run(own, 'before-request', { bru, req: ownRequest() }), /Replace both example helpRequestIds/);
});

test('own HelpRequests validate source identity, count, generation and optional id without mutating the body', () => {
  for (const changes of [
    { sessionId: '' }, { generation: 0 }, { generation: 1.5 }, { generation: '3' },
    { helpRequestIds: null }, { helpRequestIds: [ownPayload.helpRequestIds[0]] },
    { helpRequestIds: [ownPayload.helpRequestIds[0], '00000000-0000-0000-0000-000000000000'] },
    { helpRequestIds: ['c0ffee00-c0ff-ee00-c0ff-ee00c0ffee00', 'C0FFEE00-C0FF-EE00-C0FF-EE00C0FFEE00'] },
    { helpRequestIds: Array.from({ length: 101 }, (_, index) => `${String(index + 1).padStart(8, '0')}-1111-1111-1111-111111111111`) },
    { generalizationId: '' }, { generalizationId: ownPayload.helpRequestIds[0] },
    { question: 'Caller text is not part of this endpoint.' }
  ]) {
    const raw = JSON.stringify({ ...ownPayload, ...changes });
    const req = ownRequest(raw);
    assert.throws(() => run(own, 'before-request', { bru: createBru(), req }),
      /sessionId|generation|helpRequestIds|generalizationId|Remove question/, JSON.stringify(changes));
    assert.equal(req.getBody({ raw: true }), raw);
  }
});

test('own HelpRequests allow an explicit retry id and preserve existing context on failed responses', () => {
  const generated = '99999999-9999-9999-9999-999999999999';
  const runtime = { generalizationId: generated, faqDemoGeneralizationId: seedData.generalization.generalizationId };
  const environment = { generalizationId: generated };
  const bru = createBru(runtime, environment);
  const payload = { ...ownPayload, generalizationId: generated };
  const req = ownRequest(JSON.stringify(payload));

  run(own, 'before-request', { bru, req });
  assert.deepEqual(req.getBody(), payload);
  captureOwnResponse(bru, req, 400, { error: 'Invalid source' });
  assert.equal(runtime.generalizationId, generated);
  assert.equal(environment.generalizationId, generated);
  captureOwnResponse(bru, req, 202, JSON.stringify({ generalizationId: generated }));
  assert.equal(runtime.faqDemoGeneralizationId, seedData.generalization.generalizationId);
  assert.throws(() => captureOwnResponse(bru, req, 202, {}), /Mimir did not return a valid generalizationId/);
  assert.equal(runtime.generalizationId, generated);
});

test('pasted frontend payload selects its returned session for the next queue call and keeps the new FAQ id', () => {
  const runtime = {};
  const environment = { ...localVariables };
  const bru = createBru(runtime, environment);
  seedSuccessfully(bru);
  const generated = '99999999-9999-9999-9999-999999999999';
  const req = ownRequest(JSON.stringify(ownPayload));

  run(own, 'before-request', { bru, req });
  captureOwnResponse(bru, req, 202, { ...ownPayload, generalizationId: generated,
    question: 'Dette er et generaliseret spørgsmål.' });

  const queue = read('EirService/Teacher/HelpRequests/GetQueue/GetQueue.yml');
  assert.equal(bru.interpolate(queue.http.url), `http://localhost:6004/api/teachers/sessions/${ownPayload.sessionId}/queue`);
  for (const variables of [runtime, environment]) {
    assert.equal(variables.sessionId, ownPayload.sessionId);
    assert.equal(variables.sessionGeneration, String(ownPayload.generation));
    assert.equal(variables.generalizationId, generated);
    assert.equal(variables.studentHelpRequestId, '');
    assert.equal(variables.teacherHelpRequestId, '');
    assert.equal(variables.sessionCode, '');
    assert.equal(variables.teacherSessionCode, '');
  }
  assert.equal(runtime.faqDemoGeneralizationId, undefined);
  assert.equal(runtime.seedReady, false);
});

test('a full own HelpRequests response preserves source and demo ids in the same session and generation', () => {
  const runtime = {};
  const bru = createBru(runtime);
  seedSuccessfully(bru);
  const payload = { sessionId: seedData.session.id, generation: seedData.session.currentGeneration,
    helpRequestIds: seedData.generalization.helpRequestIds };
  const generated = '99999999-9999-9999-9999-999999999999';
  const req = ownRequest(JSON.stringify(payload));

  run(own, 'before-request', { bru, req });
  captureOwnResponse(bru, req, 202, { ...payload, generalizationId: generated,
    question: 'Dette er et generaliseret spørgsmål.' });

  assert.equal(runtime.generalizationId, generated);
  assert.equal(runtime.studentHelpRequestId, seedData.students[0].helpRequestId);
  assert.equal(runtime.faqDemoGeneralizationId, seedData.generalization.generalizationId);
  assert.equal(runtime.seedReady, true);
  assert.equal(runtime.sessionCode, seedData.session.code);
});
