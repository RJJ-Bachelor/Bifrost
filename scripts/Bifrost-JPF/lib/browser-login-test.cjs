// Run against a running Bifrost AppHost with the installed Chrome browser.
// Browser cookie rules must be tested in Chrome; Bruno's cookie jar cannot enforce them.
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require(path.resolve(__dirname, '../../../artifacts/login-browser/node_modules/playwright-core'));

async function main() {
  const frontend = process.env.BIFROST_FRONTEND_URL || 'http://localhost:7001';
  const gateway = process.env.BIFROST_GATEWAY_URL || 'http://localhost:6004';
  const username = process.env.BIFROST_TEACHER_USERNAME || 'knud';
  const password = process.env.BIFROST_TEACHER_PASSWORD || 'Pass123$';
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const context = await browser.newContext();
    const page = await context.newPage();
    const rejectedCookies = [];
    const cdp = await context.newCDPSession(page);
    await cdp.send('Network.enable');
    cdp.on('Network.responseReceivedExtraInfo', event => {
      for (const cookie of event.blockedCookies || []) {
        rejectedCookies.push({ name: cookie.cookie?.name, reasons: cookie.blockedReasons });
      }
    });

    await page.goto(frontend);
    await page.getByRole('button', { name: /Log ind som/ }).first().click();
    await page.locator('[name="Input.Username"]').waitFor();
    assert.equal(await page.getByRole('heading', { name: 'Log ind', exact: true }).count(), 1);
    assert.equal(await page.locator('a[href*="ExternalLogin" i]').count(), 0);
    assert.equal(await page.getByText('Sign-in with demo.duendesoftware.com').count(), 0);

    await page.locator('[name="Input.Username"]').fill(username);
    await page.locator('[name="Input.Password"]').fill(password);
    await page.getByRole('button', { name: 'Log ind', exact: true }).click();
    await page.waitForURL(frontend + '/teachers', { timeout: 30000 });
    assert.deepEqual(rejectedCookies, [], 'Chrome must accept the authentication cookies');

    // Test both the gateway session and Angular's proxy using the browser's cookie jar.
    for (const base of [gateway, frontend]) {
      const response = await context.request.get(base + '/api/teachers/me');
      assert.equal(response.status(), 200, 'Teacher identity must work through ' + base);
      const identity = await response.json();
      assert.ok(identity.roles.includes('Teacher'));
      assert.ok(identity.userId);
    }
    await page.locator('pre').filter({ hasText: 'Teacher' }).waitFor();
    const cookies = await context.cookies();
    const identityCookie = cookies.find(cookie => cookie.name === '.AspNetCore.Identity.Application');
    assert.ok(identityCookie, 'VarService must retain the login cookie');
    assert.equal(identityCookie.sameSite, 'Lax');
    assert.ok(cookies.some(cookie => cookie.name === 'bifrost-teacher'));
    console.log('PASS: Bifrost-only login, Chrome cookies, Angular redirect and Teacher API access.');
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error.message); process.exitCode = 1; });
