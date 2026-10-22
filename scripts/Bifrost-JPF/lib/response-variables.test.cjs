const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const http = require('node:http');
const vm = require('node:vm');
const { spawn } = require('node:child_process');
const { createRequire } = require('node:module');
const { test } = require('node:test');
const yaml = require('js-yaml');
const Bru = require('@usebruno/js/src/bru');
const BrunoRequest = require('@usebruno/js/src/bruno-request');
const BrunoResponse = require('@usebruno/js/src/bruno-response');

const root = path.resolve(__dirname, '..');
const read = name => yaml.load(fs.readFileSync(path.join(root, name), 'utf8'));
const collection = read('opencollection.yml');
const requireFromCollection = createRequire(path.join(root, 'package.json'));
const session = { id: '11111111-1111-1111-1111-111111111111', code: 'STUDENT1', teacherCode: 'TEACHER1', currentGeneration: 1 };
const sourceId = '22222222-2222-2222-2222-222222222222';
const faqId = '33333333-3333-3333-3333-333333333333';
const files = {
  create: 'EirService/Teacher/Sessions/CreateSession/CreateSession.yml',
  join: 'EirService/Student/Participants/JoinSession/JoinSession.yml',
  joinTeacher: 'EirService/Teacher/Participants/JoinSession/JoinTeacherSession.yml',
  submit: 'EirService/Student/HelpRequests/CreateHelpRequest/CreateDigitalRequest.yml',
  take: 'EirService/Teacher/HelpRequests/TakeNextHelpRequest/TakeNextHelpRequest.yml',
  answer: 'EirService/Teacher/HelpRequests/AnswerHelpRequest/AnswerHelpRequest.yml',
  accept: 'EirService/Student/HelpRequests/RespondToFaq/AcceptFaq.yml',
  clear: 'EirService/Teacher/Sessions/ClearSession/ClearSession.yml',
  remove: 'EirService/Teacher/Sessions/DeleteSession/DeleteSession.yml'
};

function context(environment = {}) {
  const runtime = {};
  const bru = new Bru({ runtime: 'nodevm', collectionPath: root, runtimeVariables: runtime, envVariables: environment,
    collectionVariables: { gatewayUrl: 'http://localhost:6004' } });
  function request(name) {
    const document = read(files[name]);
    return new BrunoRequest({ method: document.http.method, url: bru.interpolate(document.http.url),
      headers: { 'Content-Type': 'application/json' }, data: document.http.body ? bru.interpolate(document.http.body.data) : undefined });
  }
  function respond(name, data, status = 200) {
    const req = request(name);
    const res = new BrunoResponse({ status, data });
    const script = collection.request.scripts.find(script => script.type === 'after-response');
    vm.runInNewContext(script.code, { bru, req, res, require: requireFromCollection });
  }
  return { bru, runtime, environment, request, respond };
}

for (const selectedEnvironment of [true, false]) {
  test(`ordinary session/request flow resolves variables with${selectedEnvironment ? '' : 'out'} Local`, () => {
    const environment = selectedEnvironment ? Object.fromEntries(read('environments/Local.yml').variables.map(v => [v.name, v.value])) : {};
    const flow = context(environment);
    flow.respond('create', session, 201);
    assert.equal(flow.request('join').getBody().code, session.code);
    assert.equal(flow.request('joinTeacher').getBody().code, session.teacherCode);
    assert.ok(flow.request('submit').getUrl().includes(session.id));
    flow.respond('join', { id: session.id, currentGeneration: 1 });
    assert.equal(flow.runtime.teacherSessionCode, session.teacherCode);
    flow.respond('submit', { id: sourceId }, 201);
    flow.respond('take', { id: faqId, isFaq: true, faqAnswerId: faqId });
    assert.ok(flow.request('answer').getUrl().includes(faqId));
    assert.ok(flow.request('accept').getUrl().includes(sourceId));
    flow.respond('answer', { id: faqId, faqAnswerId: faqId });
    assert.equal(flow.runtime.studentHelpRequestId, sourceId);
    assert.equal(environment.sessionId, session.id);
    assert.equal(environment.teacherHelpRequestId, faqId);
    flow.bru.setVar('faqDemoGeneralizationId', faqId);
    flow.respond('clear', { sessionId: session.id, currentGeneration: 2 });
    assert.equal(flow.request('clear').getBody().expectedGeneration, 2);
    assert.equal(flow.runtime.studentHelpRequestId, '');
    assert.equal(flow.runtime.teacherHelpRequestId, '');
    assert.equal(flow.runtime.generalizationId, '');
    assert.equal(flow.runtime.faqDemoGeneralizationId, undefined);
    assert.equal(flow.runtime.sessionCode, session.code);
  });
}

test('switching sessions clears stale request ids and codes; failed/invalid responses preserve context', () => {
  const flow = context();
  flow.respond('create', session, 201);
  flow.respond('submit', { id: sourceId }, 201);
  const before = { ...flow.runtime };
  flow.respond('create', { error: 'Conflict' }, 409);
  assert.deepEqual(flow.runtime, before);
  assert.throws(() => flow.respond('create', { id: 'invalid', currentGeneration: 1 }, 201), /session response/);
  assert.deepEqual(flow.runtime, before);
  flow.respond('join', { id: faqId, currentGeneration: 3 });
  assert.equal(flow.runtime.sessionId, faqId);
  assert.equal(flow.runtime.teacherSessionCode, '');
  assert.equal(flow.runtime.studentHelpRequestId, '');
  flow.respond('remove', undefined, 204);
  assert.equal(flow.runtime.sessionId, '');
  assert.equal(flow.runtime.sessionGeneration, '');
});

test('empty take-next clears the old teacher selection without changing the student request', () => {
  const flow = context();
  flow.respond('create', session, 201);
  flow.respond('submit', { id: sourceId }, 201);
  flow.respond('take', { id: faqId });
  flow.respond('take', undefined, 204);
  assert.equal(flow.runtime.teacherHelpRequestId, '');
  assert.equal(flow.runtime.studentHelpRequestId, sourceId);
});

test('the actual Bruno CLI inherits collection capture scripts across a complete request sequence', { timeout: 60000 }, async () => {
  const received = [];
  const server = http.createServer(async (req, res) => {
    let raw = '';
    for await (const chunk of req) raw += chunk;
    const body = raw ? JSON.parse(raw) : null;
    received.push({ method: req.method, url: req.url, body });
    res.setHeader('Content-Type', 'application/json');
    const sessionPath = `/api/teachers/sessions/${session.id}`;
    let data;
    if (req.url === '/api/teachers/sessions') { res.statusCode = 201; data = session; }
    else if (req.url === '/api/teachers/sessions/join' && body.code === session.teacherCode) data = session;
    else if (req.url === '/api/students/sessions/join' && body.code === session.code) data = { id: session.id, currentGeneration: 1 };
    else if (req.url === `/api/students/sessions/${session.id}/requests`) { res.statusCode = 201; data = { id: sourceId }; }
    else if (req.url === sessionPath + '/queue/take-next') data = { id: faqId, faqAnswerId: faqId };
    else if (req.url === `/api/teachers/requests/${faqId}/messages`) data = { id: faqId, faqAnswerId: faqId };
    else if (req.url === `/api/students/requests/${sourceId}/faq`) data = { id: sourceId, faqAnswerId: faqId };
    else if (req.url === sessionPath + '/clear' && body.expectedGeneration === 1) data = { sessionId: session.id, currentGeneration: 2 };
    else if (req.url === sessionPath && req.method === 'DELETE') res.statusCode = 204;
    else { res.statusCode = 400; data = { error: 'Unresolved or incorrect request variables' }; }
    res.end(data === undefined ? undefined : JSON.stringify(data));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'bifrost-bruno-variables-'));
  try {
    fs.mkdirSync(path.join(temporary, 'lib'));
    fs.copyFileSync(path.join(__dirname, 'response-variables.cjs'), path.join(temporary, 'lib/response-variables.cjs'));
    const fixture = structuredClone(collection);
    fixture.request.variables = [{ name: 'gatewayUrl', value: `http://127.0.0.1:${server.address().port}` }];
    fs.writeFileSync(path.join(temporary, 'opencollection.yml'), yaml.dump(fixture));
    fs.mkdirSync(path.join(temporary, 'environments'));
    const local = read('environments/Local.yml');
    local.variables.find(variable => variable.name === 'gatewayUrl').value = fixture.request.variables[0].value;
    fs.writeFileSync(path.join(temporary, 'environments/Local.yml'), yaml.dump(local));
    for (const [index, name] of Object.keys(files).entries()) {
      const document = read(files[name]);
      document.info.seq = index + 1;
      fs.writeFileSync(path.join(temporary, `${index + 1}-${name}.yml`), yaml.dump(document));
    }
    const cliPackage = require('@usebruno/cli/package.json');
    const cli = path.resolve(path.dirname(require.resolve('@usebruno/cli/package.json')), cliPackage.bin.bru);
    const output = await new Promise((resolve, reject) => {
      const child = spawn(process.execPath, [cli, 'run', '--env', 'Local', '--sandbox=developer', '--bail'], { cwd: temporary, windowsHide: true, timeout: 45000 });
      let text = '';
      child.stdout.on('data', chunk => text += chunk);
      child.stderr.on('data', chunk => text += chunk);
      child.on('error', reject);
      child.on('close', code => code === 0 ? resolve(text) : reject(new Error(text)));
    });
    assert.equal(received.length, Object.keys(files).length, output);
    assert.ok(received.every(request => !request.url.includes('{{')));
    assert.equal(received[5].url, `/api/teachers/requests/${faqId}/messages`);
    assert.equal(received[6].url, `/api/students/requests/${sourceId}/faq`);
  } finally {
    await new Promise(resolve => server.close(resolve));
    assert.equal(path.dirname(path.resolve(temporary)), path.resolve(os.tmpdir()));
    assert.ok(path.basename(temporary).startsWith('bifrost-bruno-variables-'));
    fs.rmSync(temporary, { recursive: true, force: true });
  }
});
