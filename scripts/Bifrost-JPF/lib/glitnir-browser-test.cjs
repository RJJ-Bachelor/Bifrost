const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require(path.resolve(__dirname, '../../../artifacts/login-browser/node_modules/playwright-core'));

async function main() {
  const frontend = process.env.BIFROST_FRONTEND_URL || 'http://localhost:7000';
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const context = await browser.newContext();
    const page = await context.newPage({ viewport: { width: 1440, height: 900 } });
    const apiCalls = [];
    page.on('request', request => {
      if (new URL(request.url()).pathname.startsWith('/api/')) apiCalls.push(request.url());
    });
    const responseFor = kind => page.waitForResponse(response =>
      response.url() === frontend + '/api/' + kind + '/me');

    await page.goto(frontend, { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: 'Velkommen' }).waitFor();
    assert.equal(apiCalls.length, 0, 'The home page must not fetch API data');
    await page.screenshot({ path: 'artifacts/glitnir-home.png', fullPage: true });

    let response = responseFor('students');
    await page.getByRole('link', { name: 'Student', exact: true }).click();
    const studentResponse = await response;
    assert.equal(studentResponse.status(), 200);
    const student = await studentResponse.json();
    assert.ok(student.userId);
    assert.ok(student.roles.includes('Student'));
    const cookie = (await context.cookies()).find(cookie => cookie.name === 'bifrost-anonymous-user');
    assert.ok(cookie?.httpOnly, 'The anonymous student cookie must be present and HttpOnly');
    response = responseFor('students');
    await page.getByRole('button', { name: 'Hent igen' }).click();
    assert.equal((await (await response).json()).userId, student.userId, 'Reload must keep the student identity');
    assert.equal((await context.cookies()).find(cookie => cookie.name === 'bifrost-anonymous-user').value, cookie.value);

    response = responseFor('teachers');
    await page.getByRole('link', { name: 'Teacher', exact: true }).click();
    assert.equal((await response).status(), 401, 'Teacher data must reject anonymous access');
    await page.getByRole('alert').waitFor();
    assert.equal(new URL(page.url()).pathname, '/teacher', 'Do not redirect to login automatically');
    await page.screenshot({ path: 'artifacts/glitnir-teacher-denied.png', fullPage: true });

    await page.getByRole('button', { name: 'Login', exact: true }).click();
    await page.locator('[name="Input.Username"]').fill(process.env.BIFROST_TEACHER_USERNAME || 'knud');
    await page.locator('[name="Input.Password"]').fill(process.env.BIFROST_TEACHER_PASSWORD || 'Pass123$');
    response = responseFor('teachers');
    await page.getByRole('button', { name: 'Log ind', exact: true }).click();
    await page.waitForURL(frontend + '/teacher');
    const teacherResponse = await response;
    assert.equal(teacherResponse.status(), 200);
    assert.ok((await teacherResponse.json()).roles.includes('Teacher'));
    await page.locator('pre').filter({ hasText: 'Teacher' }).waitFor();

    // Returning home must not refresh or query the signed-in user's identity.
    const before = apiCalls.length;
    await page.getByRole('link', { name: 'Forside', exact: true }).click();
    await page.getByRole('heading', { name: 'Velkommen' }).waitFor();
    assert.equal(apiCalls.length, before);
    console.log('PASS: Passive home, student cookie reuse, Teacher 401, VarService login and Teacher 200.');
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error.message); process.exitCode = 1; });
