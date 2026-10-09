const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const yaml = require('js-yaml');
let requests = 0, scripts = 0;
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
    }
  }
}
scan(path.resolve(__dirname, '..'));
console.log(`Validated YAML and JavaScript: ${requests} requests, ${scripts} scripts.`);
