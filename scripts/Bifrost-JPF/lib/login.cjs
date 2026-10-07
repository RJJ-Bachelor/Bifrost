const http = require('node:http');
const https = require('node:https');

// A private cookie jar per login keeps the teacher, learner and Bruno identities separate.
// Follow the real Razor login form, including its antiforgery token and OIDC callback.
module.exports = async function login({ gatewayUrl, identityUrl, username, password }) {
  if (!username || !password) throw new Error('Set the login username and secret password in Local. See README.md.');
  const allowedOrigins = new Set([new URL(gatewayUrl).origin, new URL(identityUrl).origin]);
  const cookies = new Map();
  const cookieHeader = (url) => [...cookies.values()]
    .filter(c => c.host === url.hostname && url.pathname.startsWith(c.path) && (!c.secure || url.protocol === 'https:'))
    .map(c => c.pair).join('; ');

  async function send(address, data) {
    const url = new URL(address);
    if (!allowedOrigins.has(url.origin)) throw new Error('Login redirected outside gatewayUrl/identityUrl.');
    const response = await new Promise((resolve, reject) => {
      const body = data ? new URLSearchParams(data).toString() : '';
      const headers = { Cookie: cookieHeader(url) };
      if (data) {
        headers['Content-Type'] = 'application/x-www-form-urlencoded';
        headers['Content-Length'] = Buffer.byteLength(body);
      }
      const request = (url.protocol === 'https:' ? https : http).request(url, {
        method: data ? 'POST' : 'GET', headers
      }, res => {
        let html = '';
        res.setEncoding('utf8');
        res.on('data', chunk => { html += chunk; });
        res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, html }));
      });
      request.setTimeout(30000, () => request.destroy(new Error('Login request timed out.')));
      request.on('error', reject);
      request.end(body);
    });
    for (const value of response.headers['set-cookie'] || []) {
      const [pair, ...attributes] = value.split(';');
      const key = pair.slice(0, pair.indexOf('='));
      const path = (attributes.find(a => /^\s*path=/i.test(a)) || 'path=/').trim().slice(5);
      const mapKey = url.hostname + path + ':' + key;
      if (attributes.some(a => /^\s*max-age=0$/i.test(a)) || pair.endsWith('=')) cookies.delete(mapKey);
      else cookies.set(mapKey, { host: url.hostname, path, pair, secure: attributes.some(a => /^\s*secure$/i.test(a)) });
    }
    return response;
  }

  function decode(value) {
    return value.replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#x([a-f0-9]+);/gi, (_, n) => String.fromCharCode(parseInt(n, 16)))
      .replace(/&#([0-9]+);/g, (_, n) => String.fromCharCode(Number(n)));
  }
  function field(html, name) {
    for (const input of html.match(/<input\b[^>]*>/gi) || []) {
      const attributes = Object.fromEntries([...input.matchAll(/([\w-]+)="([^"]*)"/g)].map(m => [m[1], decode(m[2])]));
      if (attributes.name === name) return attributes.value || '';
    }
    throw new Error('VarService login form is missing ' + name);
  }

  let address = gatewayUrl + '/account/login';
  let response = await send(address);
  if (response.status !== 302) throw new Error('Gateway did not start OpenID Connect login.');
  const authorization = new URL(response.headers.location, address);
  const pushed = authorization.searchParams.has('request_uri');
  if (authorization.origin !== new URL(identityUrl).origin || authorization.pathname !== '/connect/authorize' ||
      authorization.searchParams.get('client_id') !== 'interactive' ||
      (!pushed && (authorization.searchParams.get('response_type') !== 'code' || authorization.searchParams.get('code_challenge_method') !== 'S256'))) {
    throw new Error('Gateway must use VarService authorization code with PKCE.');
  }

  for (let step = 0; step < 15; step++) {
    if (response.status === 302 || response.status === 303) {
      address = new URL(response.headers.location, address).href;
      const target = new URL(address);
      if (target.origin === new URL(gatewayUrl).origin && target.pathname === '/api/teachers/me') {
        const session = cookieHeader(target);
        if (!session.includes('bifrost-teacher=')) throw new Error('Gateway did not issue a teacher session.');
        return session;
      }
      response = await send(address);
      continue;
    }
    if (response.status === 200 && /name="Input.Username"/.test(response.html)) {
      response = await send(address, {
        'Input.Username': username, 'Input.Password': password,
        'Input.ReturnUrl': field(response.html, 'Input.ReturnUrl'),
        'Input.Button': 'login', '__RequestVerificationToken': field(response.html, '__RequestVerificationToken')
      });
      if (response.status !== 302) throw new Error('VarService rejected the credentials for ' + username);
      continue;
    }
    throw new Error('Unexpected login response ' + response.status + ' at ' + new URL(address).pathname);
  }
  throw new Error('Too many login redirects.');
};
