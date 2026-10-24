// Shared by the collection response script and development setup requests.
// Runtime variables make the next request work immediately; environment values
// expose the same context in Local. Never copy authentication data here.
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const validId = value => typeof value === 'string' && guid.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value);
const requestVariables = ['helpRequestId', 'studentHelpRequestId', 'teacherHelpRequestId',
  'generalizationId', 'firstHelpRequestId', 'secondHelpRequestId'];

function save(bru, variables) {
  for (const [name, value] of Object.entries(variables)) {
    bru.setVar(name, String(value));
    bru.setEnvVar(name, String(value));
  }
}

function current(bru, name) {
  return bru.getVar(name) ?? bru.getEnvVar(name);
}

function clearRequests(bru) {
  save(bru, Object.fromEntries(requestVariables.map(name => [name, ''])));
  for (const name of ['faqDemoSessionId', 'faqDemoGeneration', 'faqDemoGeneralizationId',
    'faqDemoFirstHelpRequestId', 'faqDemoSecondHelpRequestId']) bru.deleteVar(name);
  bru.setVar('seedReady', false);
  bru.setVar('faqDemoTakenRequestId', null);
}

function captureSession(bru, data, { reset = false, code, teacherCode } = {}) {
  const id = data?.id ?? data?.sessionId;
  if (!validId(id) || !Number.isInteger(data?.currentGeneration) || data.currentGeneration < 1)
    throw new Error('The session response is missing a valid id or currentGeneration. Existing variables were preserved.');
  const changed = current(bru, 'sessionId') !== id;
  if (changed) save(bru, { archiveId: '' });
  if (reset || changed || String(current(bru, 'sessionGeneration')) !== String(data.currentGeneration))
    clearRequests(bru);
  save(bru, {
    sessionId: id, sessionGeneration: data.currentGeneration,
    sessionCode: data.code ?? code ?? (changed ? '' : current(bru, 'sessionCode') ?? ''),
    teacherSessionCode: data.teacherCode ?? teacherCode ?? (changed ? '' : current(bru, 'teacherSessionCode') ?? '')
  });
}

function captureHelpRequest(bru, data, role) {
  if (!validId(data?.id))
    throw new Error('The help request response is missing a valid id. Existing variables were preserved.');
  if (role === 'students') {
    save(bru, { studentHelpRequestId: data.id, helpRequestId: data.id });
    // Allow the teacher to inspect a newly created request before reserving it.
    if (!current(bru, 'teacherHelpRequestId')) save(bru, { teacherHelpRequestId: data.id });
  } else save(bru, { teacherHelpRequestId: data.id });
  if (validId(data.faqAnswerId)) save(bru, { generalizationId: data.faqAnswerId });
}

function captureResponse(bru, req, res) {
  if (res.status < 200 || res.status >= 300) return;
  const route = new URL(bru.interpolate(req.getUrl())).pathname;
  const method = req.getMethod().toUpperCase();
  const sessionRoute = route.match(/^\/api\/(students|teachers)\/sessions(?:\/(join|[0-9a-f-]+)(\/(?:clear|archive))?)?$/i);
  const archiveRoute = route.match(/^\/api\/(students|teachers)\/sessions\/[0-9a-f-]+\/archives\/[0-9a-f-]+$/i);
  const helpRequestRoute = route.match(/^\/api\/(students|teachers)\/(?:requests\/[0-9a-f-]+(?:\/(?:messages|status|faq))?|sessions\/[0-9a-f-]+\/(?:requests|queue\/take-next))$/i);
  const publish = /^\/api\/teachers\/development\/generalizations(?:\/from-help-requests)?$/.test(route) && method === 'POST';
  // The seed request validates its full payload and sets demo cookies itself.
  if (!sessionRoute && !helpRequestRoute && !archiveRoute && !publish) return;
  if (sessionRoute && method === 'DELETE') {
    if (sessionRoute[2] === current(bru, 'sessionId')) {
      clearRequests(bru);
      save(bru, { sessionId: '', sessionCode: '', teacherSessionCode: '', sessionGeneration: '', archiveId: '' });
    }
    return;
  }
  if (res.status === 204) {
    // An empty queue must not leave the previous reservation selected for Answer.
    if (helpRequestRoute && route.endsWith('/queue/take-next')) save(bru, { teacherHelpRequestId: '' });
    return;
  }
  const body = res.getBody();
  const data = typeof body === 'string' ? JSON.parse(body) : body;
  if (publish) {
    if (!validId(data?.generalizationId)) throw new Error('Mimir did not return a valid generalizationId.');
    if (route.endsWith('/from-help-requests') && data.sessionId != null && data.generation != null)
      captureSession(bru, { id: data.sessionId, currentGeneration: data.generation });
    // Capture the id after a session change clears the previous request context.
    save(bru, { generalizationId: data.generalizationId });
  } else if (archiveRoute) {
    if (!validId(data?.id)) throw new Error('The archive response is missing a valid id. Existing variables were preserved.');
    save(bru, { archiveId: data.id });
  } else if (helpRequestRoute) captureHelpRequest(bru, data, helpRequestRoute[1]);
  else if (sessionRoute && !Array.isArray(data)) {
    if (sessionRoute[3] === '/archive' && !validId(data?.archiveId))
      throw new Error('The archive response is missing a valid archiveId. Existing variables were preserved.');
    const joining = sessionRoute[2] === 'join' ? req.getBody() : null;
    captureSession(bru, data, { reset: Boolean(sessionRoute[3]),
      code: sessionRoute[1] === 'students' ? joining?.code : undefined,
      teacherCode: sessionRoute[1] === 'teachers' ? joining?.code : undefined });
    if (sessionRoute[3] === '/archive') save(bru, { archiveId: data.archiveId });
  }
}

module.exports = { captureResponse, captureSession, captureHelpRequest, save };
