// Push only: requests, including authenticated API responses, stay on the network.
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('push', event => {
  let notification;
  try {
    notification = event.data?.json()?.notification;
  } catch {
    // A malformed payload must still produce a visible notification.
  }

  const text = value => typeof value === 'string' && value.trim() ? value : undefined;
  event.waitUntil(self.registration.showNotification(text(notification?.title) ?? 'Bifrost', {
    body: text(notification?.body) ?? 'Du har modtaget en ny besked. Åbn Bifrost for at se den.',
    tag: text(notification?.tag),
    icon: '/icons/icon-192.png',
    badge: '/icons/badge-96.png',
    data: notification?.data
  }));
});

self.addEventListener('notificationclick', event => {
  event.notification.close();
  event.waitUntil((async () => {
    const scope = new URL(self.registration.scope);
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const client of windows) {
      const url = new URL(client.url);
      if (url.origin !== scope.origin || !url.pathname.startsWith(scope.pathname)) continue;
      try {
        await client.focus();
        return;
      } catch {
        // A tab may close between matching and focusing it.
      }
    }

    // The payload has no recipient role. Open the app without guessing an identity
    // or following any URL supplied in notification data.
    await self.clients.openWindow(new URL('/', scope).href);
  })());
});
