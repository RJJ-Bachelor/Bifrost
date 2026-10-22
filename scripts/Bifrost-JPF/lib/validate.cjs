const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const yaml = require('js-yaml');
let requests = 0, scripts = 0;
const eirRequests = new Set();
const eirCollection = path.resolve(__dirname, '../EirService') + path.sep;
const normalizeRoute = route => route.replace(/^\{\{gatewayUrl\}\}/, '').split('?')[0]
  .replace(/\{\{[^}]+\}\}/g, '{}').replace(/\{[^}]+\}/g, '{}');
function scan(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (['node_modules', '.git'].includes(entry.name)) continue;
    const filename = path.join(directory, entry.name);
    if (entry.isDirectory()) { scan(filename); continue; }
    if (!filename.endsWith('.yml')) continue;
    const document = yaml.load(fs.readFileSync(filename, 'utf8'), { filename });
    const code = [...(document.request?.scripts || []), ...(document.runtime?.scripts || [])];
    for (const script of code) {
      new vm.Script('(async function () {\n' + script.code + '\n})', { filename });
      scripts++;
    }
    if (document.http) {
      requests++;
      if (!document.http.url.startsWith('{{')) throw new Error(filename + ': use an environment URL');
      if (document.settings.followRedirects !== false) throw new Error(filename + ': redirects must be disabled');
      if (filename.startsWith(eirCollection))
        eirRequests.add(document.http.method + ' ' + normalizeRoute(document.http.url));
    }
  }
}
scan(path.resolve(__dirname, '..'));
console.log(`Validated YAML and JavaScript: ${requests} requests, ${scripts} scripts.`);

const apiDirectory = path.resolve(__dirname, '../../../src/Bifrost/Backend/EirService/EirService.Api/Endpoints');
if (fs.existsSync(apiDirectory)) {
  const apiRoutes = new Set();
  function readApi(directory) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      if (entry.name === 'Test') continue; // Development endpoints use separate gateway routes.
      const filename = path.join(directory, entry.name);
      if (entry.isDirectory()) { readApi(filename); continue; }
      if (!filename.endsWith('.cs')) continue;
      const code = fs.readFileSync(filename, 'utf8');
      for (const [, method, route] of code.matchAll(/\b(Get|Post|Patch|Put|Delete)\(\s*"([^"]+)"/g))
        apiRoutes.add(method.toUpperCase() + ' ' + normalizeRoute('/api' + route));
    }
  }
  readApi(apiDirectory);
  const missing = [...apiRoutes].filter(route => !eirRequests.has(route));
  const unknown = [...eirRequests].filter(route => !apiRoutes.has(route));
  if (missing.length || unknown.length)
    throw new Error(`EirService route mismatch. Missing Bruno routes: ${missing.join(', ') || 'none'}. Unknown Bruno routes: ${unknown.join(', ') || 'none'}.`);
  console.log(`Validated EirService API coverage: ${apiRoutes.size} routes.`);
}
