'use strict';

// Serverer en bygget Angular-app og videresender /api og /account til gatewayen
// (samme stier som proxy.conf.json), saa frontend og backend deler origin.
// WebSockets (SignalR) under /api videresendes ogsaa.
//
// Brug: node pwa-server.js <dist-mappe> [port] [gateway-url]

const http = require('http');
const net = require('net');
const fs = require('fs');
const path = require('path');

const root = path.resolve(process.argv[2] || 'dist');
const port = Number(process.argv[3] || 8080);
const target = new URL(process.argv[4] || 'http://localhost:6004');

const proxiedPrefixes = ['/api', '/account'];

const mimeTypes = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.webmanifest': 'application/manifest+json; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.wasm': 'application/wasm',
};

const log = (...args) => console.log(new Date().toLocaleTimeString('da-DK'), ...args);

const isProxied = (url) => {
  const pathname = url.split('?')[0].toLowerCase();
  return proxiedPrefixes.some((prefix) => pathname === prefix || pathname.startsWith(prefix + '/'));
};

// Som changeOrigin i proxy.conf.json: gatewayen ser sin egen adresse som Host.
// Det oprindelige host/skema sendes med i X-Forwarded-*.
const forwardHeaders = (req) => ({
  ...req.headers,
  host: target.host,
  'x-forwarded-proto': req.headers['x-forwarded-proto'] || 'http',
  'x-forwarded-host': req.headers['x-forwarded-host'] || req.headers.host || '',
});

function proxyHttp(req, res) {
  const upstream = http.request(
    {
      hostname: target.hostname,
      port: target.port,
      path: req.url,
      method: req.method,
      headers: forwardHeaders(req),
    },
    (upstreamRes) => {
      res.writeHead(upstreamRes.statusCode, upstreamRes.statusMessage, upstreamRes.headers);
      upstreamRes.pipe(res);
    }
  );
  upstream.on('error', (err) => {
    log(`${req.method} ${req.url} -> 502 (${err.code || err.message})`);
    if (!res.headersSent) {
      res.writeHead(502, { 'content-type': 'text/plain; charset=utf-8' });
    }
    res.end(`Gatewayen paa ${target.origin} svarer ikke (${err.code || err.message}). Koerer aspire run?`);
  });
  res.on('finish', () => log(`${req.method} ${req.url} -> ${res.statusCode}`));
  req.pipe(upstream);
}

async function resolveFile(pathname) {
  const candidate = path.join(root, pathname);
  if (candidate !== root && !candidate.startsWith(root + path.sep)) return null;
  try {
    const stat = await fs.promises.stat(candidate);
    if (stat.isFile()) return candidate;
  } catch {
    // findes ikke
  }
  // En manglende fil med endelse (fx .js) er en 404. Resten er Angular-ruter.
  if (path.extname(pathname)) return null;
  return path.join(root, 'index.html');
}

async function serveStatic(req, res) {
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    res.writeHead(405, { allow: 'GET, HEAD' });
    return res.end();
  }
  let pathname;
  try {
    pathname = decodeURIComponent(req.url.split('?')[0]);
  } catch {
    res.writeHead(400);
    return res.end();
  }
  const file = await resolveFile(pathname);
  if (!file) {
    log(`${req.method} ${req.url} -> 404`);
    res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' });
    return res.end('Ikke fundet');
  }
  res.writeHead(200, {
    'content-type': mimeTypes[path.extname(file).toLowerCase()] || 'application/octet-stream',
    'cache-control': 'no-cache',
  });
  if (req.method === 'HEAD') return res.end();
  fs.createReadStream(file)
    .on('error', () => res.destroy())
    .pipe(res);
}

const server = http.createServer(async (req, res) => {
  try {
    if (isProxied(req.url)) return proxyHttp(req, res);
    await serveStatic(req, res);
  } catch (err) {
    log(`${req.method} ${req.url} -> 500 (${err.message})`);
    if (!res.headersSent) res.writeHead(500);
    res.end();
  }
});

// WebSockets (SignalR) under de videresendte stier
server.on('upgrade', (req, socket, head) => {
  if (!isProxied(req.url)) {
    socket.destroy();
    return;
  }
  log(`WS ${req.url}`);
  const upstream = net.connect(Number(target.port) || 80, target.hostname, () => {
    let raw = `${req.method} ${req.url} HTTP/${req.httpVersion}\r\n`;
    for (const [name, value] of Object.entries(forwardHeaders(req))) {
      for (const v of Array.isArray(value) ? value : [value]) raw += `${name}: ${v}\r\n`;
    }
    upstream.write(raw + '\r\n');
    if (head && head.length) upstream.write(head);
    upstream.pipe(socket);
    socket.pipe(upstream);
  });
  upstream.on('error', () => socket.destroy());
  socket.on('error', () => upstream.destroy());
  socket.on('close', () => upstream.destroy());
  upstream.on('close', () => socket.destroy());
});

if (!fs.existsSync(path.join(root, 'index.html'))) {
  console.error(`Fandt ikke index.html i ${root}. Er appen bygget?`);
  process.exit(1);
}

server.listen(port, () => {
  log(`Serverer ${root} paa http://localhost:${port}`);
  log(`Videresender ${proxiedPrefixes.join(', ')} til ${target.origin}`);
});
