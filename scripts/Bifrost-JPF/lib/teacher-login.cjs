const login = require('./login.cjs');
const loginOptions = require('./login-options.cjs');

module.exports = async function teacherLogin(bru, req) {
  const options = loginOptions(bru, 'teacher');
  const header = await login(options);
  const sessionCookies = header.split(/;\s*/).filter(pair => /^bifrost-teacher(?:C\d+)?=/.test(pair));
  const gatewayUrl = options.gatewayUrl.replace(/\/+$/, '') + '/';
  const jar = bru.cookies.jar();

  // Replace the teacher session, keeping the student's cookie and other cookies.
  for (const cookie of await jar.getCookies(gatewayUrl)) {
    if (/^bifrost-teacher(?:C\d+)?$/.test(cookie.key)) {
      await jar.deleteCookie(gatewayUrl, cookie.key);
    }
  }

  for (const pair of sessionCookies) {
    const separator = pair.indexOf('=');
    await jar.setCookie(gatewayUrl, {
      key: pair.slice(0, separator),
      value: pair.slice(separator + 1),
      path: '/',
      httpOnly: true,
      secure: gatewayUrl.startsWith('https://'),
      sameSite: 'lax'
    });
  }

  req.setHeader('Cookie', sessionCookies.join('; '));
};
