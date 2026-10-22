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
