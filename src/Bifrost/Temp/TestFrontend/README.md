# Bifrost testfrontend

En simpel side til manuelle kald til EirService gennem Heimdall, med de samme endpoints som Bruno.

Start backendservices, Heimdall på `http://localhost:6004` og VarService på `http://localhost:6003`.
Kør `npm start` i denne mappe og åbn **http://localhost:7003**. Brug `localhost`, så login-cookies deles med Heimdall. Aspire kan også starte frontenden på sin konfigurerede frontend-port.

## Prøv et forløb

1. Gå til **Lærer**, log ind med en Teacher-konto, og opret en session med en titel.
2. Sessionens ID, kode og generation bliver automatisk valgt øverst på siden.
3. Gå til **Studerende**, og tryk **Tilmeld som studerende**. Et API-svar bekræfter tilmeldingen.
4. Gå til **Hjælpeanmodninger**, skriv en besked, og send den med den valgte session.
5. Hent hjælpeanmodningerne for sessionen.
6. Gå til **Lærer**, og ryd sessionen. Generationen opdateres automatisk fra svaret.
7. Hent hjælpeanmodningerne igen: gamle anmodninger skjules. Send en ny med den nye generation.

**Valgt session** deles mellem fanerne og gemmes i browserfanens session storage, også under lærerlogin. Du kan vælge en session fra en liste eller redigere ID, kode og generation manuelt. Et tomt ID kan ikke bruges til at hente en enkelt session.

Elevidentiteten oprettes automatisk af API'et og gemmes i en cookie. Lærerlogin bruger Heimdall og VarService. Lærerlogout bevarer elevens cookie.

## Endpoints

| Fane | Handling | Metode og route |
|---|---|---|
| Oversigt | Servicestatus | `GET /api/monitoring/alive` |
| Studerende | Elevidentitet | `GET /api/students/me` |
| Studerende | Tilmeld via kode | `POST /api/students/sessions/join` |
| Studerende | Mine tilmeldte sessions | `GET /api/students/sessions` |
| Studerende | Én valgt session | `GET /api/students/sessions/{sessionId}` |
| Hjælpeanmodninger | Opret | `POST /api/students/createhelprequest` |
| Hjælpeanmodninger | Mine anmodninger | `GET /api/students/requests` |
| Hjælpeanmodninger | Mine anmodninger i valgt session | `GET /api/students/requests?sessionId={sessionId}` |
| Lærer | Læreridentitet | `GET /api/teachers/me` |
| Lærer | Opret session | `POST /api/teachers/sessions` |
| Lærer | Mine sessions | `GET /api/teachers/sessions` |
| Lærer | Én valgt session | `GET /api/teachers/sessions/{sessionId}` |
| Lærer | Ryd session | `POST /api/teachers/sessions/{sessionId}/clear` |

Siden viser metode, URL, sendt JSON, HTTP-status, API-svar og fejl. Hjælpeanmodninger kan også sendes uden session. Der genereres et nyt besked-ID efter en vellykket oprettelse.

Alle API-kald bruger Angulars dev-proxy i `proxy.conf.json`. Login og logout navigerer til Heimdall; returadressen er den aktuelle frontend-origin plus `/teachers`.

## Build

`npm run build` bygger frontenden. Automatiske frontendtests, testkommandoen og Jasmine/Karma-opsætningen er fjernet. Angular-generatorerne er konfigureret til at springe testfiler over.

Dev-proxyen bruges af `npm start`; ved anden hosting skal `/api` routes gennem Heimdall.
