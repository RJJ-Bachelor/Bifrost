// Resolve credentials from the same scopes as request variables. Empty environment
// password placeholders may fall back to credentials configured in the collection.
module.exports = function loginOptions(bru, kind) {
  function credential(name, processName) {
    const values = [
      bru.interpolate('{{' + name + '}}'),
      bru.getCollectionVar(name),
      processName ? bru.getProcessEnv(processName) : undefined
    ];
    return values.find(value => typeof value === 'string' && value.length > 0 && !/^\{\{[^{}]+\}\}$/.test(value));
  }

  const usernameKey = kind + 'Username';
  const passwordKey = kind + 'Password';
  const processKey = 'BIFROST_' + kind.toUpperCase() + '_PASSWORD';
  const username = credential(usernameKey);
  const password = credential(passwordKey, processKey);
  const environment = bru.getEnvName() || '(none)';
  if (!username) throw new Error('Missing ' + usernameKey + '. Set it in Local or collection variables. Selected environment: ' + environment + '.');
  if (!password) throw new Error('Missing ' + passwordKey + '. Set it in Local or collection variables, or set ' + processKey + '. Selected environment: ' + environment + '.');

  return {
    gatewayUrl: bru.interpolate('{{gatewayUrl}}'),
    identityUrl: bru.interpolate('{{identityUrl}}'),
    username, password
  };
};
