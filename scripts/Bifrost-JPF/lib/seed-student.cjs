// Use the same cookie jar as the existing teacher login. Explicit Cookie headers
// alone can be overwritten by Bruno's stored student cookie.
module.exports = async function useSeedStudent(bru, req, variableName) {
  if (bru.isSafeMode()) throw new Error('Select Developer Mode in Bruno to select a demo student.');
  const cookieName = 'bifrost-anonymous-user';
  const header = bru.getVar(variableName);
  if (typeof header !== 'string' || !header.startsWith(cookieName + '=')) {
    throw new Error('Run Seed FAQ demo successfully first.');
  }
  const studentUrl = bru.interpolate('{{gatewayUrl}}').replace(/\/+$/, '') + '/api/students/';
  const jar = bru.cookies.jar();
  for (const cookie of await jar.getCookies(studentUrl)) {
    if (cookie.key === cookieName) await jar.deleteCookie(studentUrl, cookie.key);
  }
  await jar.setCookie(studentUrl, {
    key: cookieName,
    value: header.slice(cookieName.length + 1),
    path: '/api/students',
    httpOnly: true,
    secure: studentUrl.startsWith('https:'),
    sameSite: 'lax'
  });
  req.setHeader('Cookie', header);
};
