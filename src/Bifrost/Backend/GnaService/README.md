# GnaService: push til PWA

Gna forsøger først SignalR. For `RealtimeOrPush` sendes Web Push til brugerens gemte abonnementer, hvis SignalR ikke kan sende. `RealtimeOnly` sender aldrig push. Abonnementer og levering ligger i de eksisterende Domain-, Application- og Infrastructure-lag.

Fallback gælder pr. bruger: Har brugeren én aktiv SignalR-forbindelse, springes push over på **alle** brugerens enheder, også en telefon uden forbindelse. Forbindelsesregistret ligger i hukommelsen på én Gna-instans; flere instanser kræver fælles registrering af forbindelser. SignalR og push bekræfter afsendelse/accept hos transporten, ikke at telefonen har vist eller brugeren har læst beskeden.

Testfrontenden har PWA-manifest, service worker og knapper til tilmelding og afmelding. Se [testfrontendens opsætning](../../Temp/TestFrontend/README.md#pwa-og-push-beskeder). Eksemplerne nedenfor viser integrationen, hvis en anden frontend skal tilsluttes.

## Opsæt serveren

Web Push kræver ikke en særskilt konto hos Firebase, Google eller Apple. Browseren opretter abonnementet hos sin push-tjeneste. Gna identificerer sig med ét fast VAPID-nøglepar. [Apple beskriver server- og browseropsætningen her](https://developer.apple.com/documentation/usernotifications/sending-web-push-notifications-in-web-apps-and-browsers).

Til lokal udvikling aktiverer Aspire automatisk push og opretter et fast lokalt VAPID-nøglepar pr. udvikler. Nøglerne genbruges efter genstart og gemmes uden for repositoryet. AppHost sender konfigurationen videre til Gna. `Parameters:push-enabled` styrer aktiveringen (som standard `true`), og `Parameters:push-subject` er kontaktadressen som `mailto:` eller offentlig HTTPS-adresse i AppHosts settings. Genstart Aspire efter ændringer. [Testfrontendens startscript](../../Temp/TestFrontend/README.md#test-på-computer-og-telefon) starter derefter PWA og ngrok; ngroks authtoken kræver fortsat lokal opsætning. Det følgende er den manuelle opsætning til andre miljøer.

Generér nøglerne én gang i dit eget miljø:

```powershell
npx web-push generate-vapid-keys --json
```

Gem samme nøglepar permanent. Nøgler må ikke genereres ved hver opstart; skiftes de, skal browserne tilmeldes igen. Sæt følgende miljøvariabler på **GnaService** via jeres eksisterende konfiguration/secrets:

```text
Push__Enabled=true
Push__Subject=mailto:jeres-kontakt@jeres-domæne.dk
Push__PrivateKey=<privateKey fra samme nøglepar>
```

De svarer til `Push:Enabled`, `Push:Subject` og `Push:PrivateKey`. Gna beregner selv den offentlige nøgle ud fra privatnøglen og udleverer den til PWA'en via det eksisterende endpoint. En eksplicit `Push:PublicKey` understøttes fortsat og valideres mod privatnøglen. Push er som standard deaktiveret. Brug en reel kontaktadresse; alternativt kan `Subject` være en offentlig HTTPS-adresse. Privatnøglen skal kun findes på serveren. [Officiel vejledning til nøglegenerering](https://github.com/web-push-libs/web-push#command-line).

Brug den eksisterende HTTPS-gateway. Telefonen skal stole på HTTPS-certifikatet; en HTTP-adresse på det lokale netværk er ikke tilstrækkelig. På iPhone/iPad kræves iOS/iPadOS 16.4 eller nyere: Tilføj PWA'en til hjemmeskærmen, åbn den dér, og lad brugeren trykke på en tilmeldingsknap. Tilladelsen skal anmodes som følge af brugerens handling. [WebKit om iPhone/iPad](https://webkit.org/blog/13878/web-push-for-web-apps-on-ios-and-ipados/).

## API og tilmelding

| Metode | Elev-endpoint | Indhold |
| --- | --- | --- |
| GET | `/api/students/notifications/push/public-key` | Svar: `{ "publicKey": "..." }` |
| PUT | `/api/students/notifications/push/subscriptions` | Browserens `subscription.toJSON()`: `{ "endpoint": "https://...", "keys": { "p256dh": "...", "auth": "..." } }` |
| DELETE | `/api/students/notifications/push/subscriptions` | JSON: `{ "endpoint": "https://..." }` |

PUT og DELETE svarer med HTTP 204. GET svarer med HTTP 503, når push er deaktiveret. Registreringen accepterer HTTPS på port 443 uden credentials fra `fcm.googleapis.com`, `push.services.mozilla.com`, `push.apple.com` og `notify.windows.com`, inklusive underdomæner. Det begrænser serverens udgående kald til browsernes push-tjenester.

Lærere bruger de samme paths med `/api/teachers/` og deres eksisterende JWT i `Authorization: Bearer ...`. Elever bruger den eksisterende elev-cookie. Serveren udleder brugeridentiteten fra login; klienten sender ikke et bruger-id. En bruger kan registrere flere browsere/telefoner.

Eksemplet er almindelig JavaScript til en PWA med egen service worker. Servér worker-filen som `/push-sw.js`. Har appen allerede en worker, integreres håndteringen dér; en Angular-app skal bruge sin eksisterende Angular service worker frem for at registrere en ekstra worker på samme scope.

```html
<button id="enable-push" disabled>Aktivér notifikationer</button>
<button id="disable-push" disabled>Deaktivér notifikationer</button>
<p id="push-status" role="status"></p>
```

Kør dette som et JavaScript-modul efter login. Worker og nøgle klargøres, før knappen aktiveres, så `subscribe()` kan kaldes direkte ved brugerens klik:

```javascript
const pushBase = "/api/students/notifications/push";
const authHeaders = {}; // Lærer: { Authorization: `Bearer ${accessToken}` }
const status = document.querySelector("#push-status");
const enableButton = document.querySelector("#enable-push");
const disableButton = document.querySelector("#disable-push");

async function pushApi(path, options = {}) {
  const response = await fetch(pushBase + path, {
    ...options,
    credentials: "include",
    headers: {
      ...authHeaders,
      ...(options.body ? { "Content-Type": "application/json" } : {})
    }
  });
  if (!response.ok) throw new Error(`Push-registrering fejlede (${response.status}).`);
  return response;
}

function decodePublicKey(value) {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, "=");
  return Uint8Array.from(atob(padded), character => character.charCodeAt(0));
}

function saveSubscription(subscription) {
  return pushApi("/subscriptions", {
    method: "PUT",
    body: JSON.stringify(subscription.toJSON())
  });
}

async function disablePush() {
  const subscription = await registration.pushManager.getSubscription();
  if (!subscription) return;
  // Fjern på serveren, mens den gamle bruger stadig er logget ind.
  await pushApi("/subscriptions", {
    method: "DELETE",
    body: JSON.stringify({ endpoint: subscription.endpoint })
  });
  await subscription.unsubscribe();
  status.textContent = "Notifikationer er deaktiveret på denne enhed.";
}

function showError(error) {
  status.textContent = error.message;
}

if (!("serviceWorker" in navigator) || !("PushManager" in window)) {
  throw new Error("Denne browser understøtter ikke Web Push i den aktuelle tilstand.");
}
await navigator.serviceWorker.register("/push-sw.js");
const registration = await navigator.serviceWorker.ready;
const { publicKey } = await (await pushApi("/public-key")).json();
const applicationServerKey = decodePublicKey(publicKey);

// Gentag PUT efter login for at knytte et eksisterende abonnement til brugeren.
const existing = await registration.pushManager.getSubscription();
if (existing) await saveSubscription(existing);

enableButton.onclick = async () => {
  try {
    const subscription = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey
    });
    await saveSubscription(subscription);
    status.textContent = "Notifikationer er aktiveret på denne enhed.";
  } catch (error) {
    showError(error);
  }
};
disableButton.onclick = () => disablePush().catch(showError);
enableButton.disabled = false;
disableButton.disabled = false;
```

Kald og afvent `disablePush()` **før logout eller kontoskift**, mens den gamle identitet stadig er gyldig. Efter nyt login registreres abonnementet igen for den aktuelle bruger; bed kun om en ny tilladelse fra en knap, hvis der ikke allerede er et abonnement. Det forhindrer, at en delt telefon fortsat modtager den tidligere brugers beskeder. [MDN om `subscribe()`](https://developer.mozilla.org/en-US/docs/Web/API/PushManager/subscribe).

## Vis beskeden i service workeren

Gna sender følgende envelope, som også understøttes af Angulars service worker. `data` indeholder `notificationId`, `sessionId`, `helpRequestId`, `generation`, `change`, `status` og `eventType`; valgfrie værdier kan være `null`:

```javascript
{
  notification: {
    title: "Bifrost",
    body: "Forhåndsvisning af beskeden",
    tag: "notifikationens id uden bindestreger",
    data: {
      notificationId, sessionId, helpRequestId,
      generation, change, status, eventType
    }
  }
}
```

Til en PWA med egen worker kan `/push-sw.js` indeholde:

```javascript
self.addEventListener("push", event => {
  const notification = event.data?.json()?.notification ?? {
    title: "Bifrost",
    body: "Du har en ny besked."
  };
  const { title, ...options } = notification;
  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener("notificationclick", event => {
  event.notification.close();
  event.waitUntil((async () => {
    const windows = await self.clients.matchAll({
      type: "window",
      includeUncontrolled: true
    });
    const existing = windows.find(client =>
      new URL(client.url).origin === self.location.origin);
    if (existing) return existing.focus();
    return self.clients.openWindow("/");
  })());
});
```

Worker-koden viser altid en synlig notifikation; Safari kan tilbagekalde tilladelsen, hvis push bruges uden at vise en besked. Klikeksemplet åbner/fokuserer appen; frontend kan senere bruge `data` til navigation. [MDN om visning](https://developer.mozilla.org/en-US/docs/Web/API/ServiceWorkerRegistration/showNotification), [Apple om synlige beskeder](https://developer.apple.com/documentation/usernotifications/sending-web-push-notifications-in-web-apps-and-browsers).

Forhåndsvisningen begrænses til 200 Unicode-kodepunkter med en ellipse ved afkortning. Push-tjenesten må gemme beskeden i 300 sekunder. Samme notifikations-id bruges som `Topic` og `tag`, så retries kan erstatte en ventende besked og samle viste notifikationer. Alle abonnementer forsøges, også hvis ét fejler. Ved midlertidige fejl bevares leveringen i outboxen til retry; enheder, der allerede lykkedes, kan derfor modtage igen. `tag` samler visningen, men giver ikke en garanti om præcis én levering. Udløbne abonnementer fjernes ved HTTP 404/410. Transporten bruger [Lib.Net.Http.WebPush](https://github.com/tpeczek/Lib.Net.Http.WebPush).

## Verificér på en rigtig telefon

1. Konfigurér VAPID-nøglerne og aktivér push på Gna. Åbn PWA'en via gatewayens betroede HTTPS-adresse; på iPhone skal den åbnes fra hjemmeskærmen.
2. Log ind, tryk **Aktivér notifikationer**, og godkend browserens tilladelse. Kontrollér, at GET af offentlig nøgle og PUT af abonnement lykkes.
3. Luk appen og alle andre faner/enheder med SignalR for samme bruger. Vent, til Gna har registreret afbrydelsen. Udløs en relevant besked med `RealtimeOrPush`; telefonen skal vise en notifikation, og klik skal åbne/fokusere appen.
4. Åbn appen med en aktiv SignalR-forbindelse og gentag. Beskeden skal modtages via SignalR uden en ekstra push. Kontrollér også, at `RealtimeOnly` ikke giver push, når brugeren er offline.
5. Tryk **Deaktivér notifikationer**, kontrollér DELETE, og gentag offline-scenariet. Tilmeld igen, og kontrollér logout/kontoskift, så den gamle brugers abonnement fjernes før identiteten skifter.

Push er afhængig af browserens og telefonens tilladelser, netværk og notifikationsindstillinger. Backendtests kan kontrollere transport og fallback, men erstatter ikke denne kontrol på en fysisk telefon.
