# Bifrost testfrontend

En simpel side til manuelle kald til EirService gennem Heimdall, med de samme endpoints som Bruno.

Start backendservices, Heimdall på `http://localhost:6004` og VarService på `http://localhost:6003`.
Kør `npm start` i denne mappe og åbn **http://localhost:7003**. Brug `localhost`, så login-cookies deles med Heimdall. Aspire kan også starte frontenden på sin konfigurerede frontend-port.

## Separate sider for elev og lærer

| Fane | Frontend-URL | Indhold |
|---|---|---|
| Lærer: sessioner | `/teachers` | Login, opret og ryd sessioner |
| Lærer: hjælp | `/teachers/requests` | Vælg session, se køen, Tag næste og besvar / start / afslut hjælp |
| Studerende: sessioner | `/students` | Elevidentitet, tilmelding og sessioner |
| Studerende: hjælp | `/students/requests` | Mine tilmeldte sessioner, opret anmodning, køplacering og samtale |

Hjælpevisningerne ligger hver for sig i `src/app/student/help-requests` og
`src/app/teacher/help-requests`. API-kaldene bruger henholdsvis `/api/students` og
`/api/teachers`. Samtalevisning og kald deler den eksisterende controller og service.
Den gamle frontend-URL `/requests` viderestiller til `/students/requests`.

Elevens tilmeldte sessioner hentes automatisk på hjælpesiden. Vælg en session fra
listen for at hente dens anmodninger og stille et spørgsmål. Det er også muligt at
tilmelde sig en session med en kode direkte på hjælpesiden. En anmodning åbner sin
samtale med første spørgsmål; **Send ny besked** tilføjer en opfølgning i samme samtale.

Lærerens hjælpeside henter identitet, egne sessioner og den valgte kø automatisk.
**Tag næste** reserverer og åbner næste ledige anmodning, klar til hjælp eller svar.

## Test med flere brugere

1. Gå til **Lærer: sessioner**, log ind med en Teacher-konto, og opret en session med en titel.
2. Sessionens ID, kode og generation bliver automatisk valgt øverst på siden.
3. Kopiér **Sessionslink til de andre elever** fra Valgt session.
4. Åbn linket i to forskellige browserprofiler eller browsere, fx en Chrome-profil og en Edge-profil. Brug én profil til hver elev.
5. Tryk **Tilmeld som studerende** i begge elevprofiler. Session-ID og kode er udfyldt fra linket. Kontrollér, at de viste Student-ID'er er forskellige.
6. Gå til **Studerende: hjælp** i hver elevprofil, vælg sessionen fra listen, og opret én anmodning. Den første besked og samtalen vises med det samme.
7. Tryk **Hent min køplacering** hos begge elever. De får deres egen placering, antal ventende og tidsestimat.
8. Gå til **Lærer: hjælp** i lærerprofilen, vælg sessionen, og tryk **Hent lærerkø**. Begge elevers anmodninger vises i køorden.
9. Tryk **Tag næste**. Anmodningen reserveres og vælges som samtale. Ved fysisk hjælp bruges **Start fysisk hjælp** og derefter **Afslut fysisk hjælp**. Ved digital hjælp skrives et svar i samtalen, og **Send digitalt svar** afslutter anmodningen.
10. Hent køplaceringen og samtalen igen hos eleven for at se ændringen. Send eventuelt en opfølgende besked: samme samtale genåbnes bagest i køen.

Almindelige faner i samme browserprofil deler elevcookie og er derfor **samme elev**.
Flere private vinduer kan også dele cookie. Brug separate profiler eller browsere til flere elever.
Der er ingen elevvælger; hver profil bruger sin egen cookie fra Heimdall.

Køen opdateres manuelt med **Hent lærerkø** eller **Hent min køplacering**. Lærerens egne handlinger opdaterer den viste anmodning og den allerede hentede kø. Ændringer fra andre profiler hentes med knapperne.

**Annuller anmodning** hos eleven lukker samtalen som `Cancelled` og fjerner den fra køen. Eleven kan annullere før fysisk hjælp starter og skal oprette en ny anmodning efter annullering.

**Send forrest i køen** hos læreren bruger samme `action: Cancel`, men sætter status til `Pending`, frigiver reservationen og returnerer anmodningen forrest. Det virker også under aktiv fysisk hjælp. Den senest returnerede anmodning kommer først; en ny besked fra eleven sender stadig anmodningen bagest.

Hver anmodning får et fast nummer som **BF-001**, **BF-002** osv., der vises ved oprettelse, i køen og i samtalen efter **Tag næste**. API'et returnerer både `requestNumber` og `displayNumber`. Nummeret følger samtalen ved returnering og genåbning. Numrene starter forfra i hver session og efter **Ryd session**. Køplaceringen vises separat og kan ændre sig. GUID'en bruges fortsat i API-routes.

**Ryd session** øger generationen. Hent køen eller sessionen igen i elevprofilerne for at opdatere generationen. Gamle anmodninger skjules fra listerne; de kan stadig læses ved at indsætte deres ID i **Valgt samtale**. Eleverne forbliver tilmeldt og kan oprette nye anmodninger.

**Valgt session** deles mellem fanerne og gemmes i browserfanens session storage, også under lærerlogin. Du kan vælge en session fra en liste eller redigere ID, kode og generation manuelt. Et tomt ID kan ikke bruges til at hente en enkelt session.

Anmodningens ID gemmes separat for elev og lærer i session storage, så den samme samtale kan hentes efter navigation eller genindlæsning. API'et kontrollerer ejerskab, reservation og generation.

Elevidentiteten oprettes automatisk af API'et og gemmes i en cookie. Lærerlogin bruger Heimdall og VarService. Lærerlogout bevarer elevens cookie.

## Endpoints

| Fane | Handling | Metode og route |
|---|---|---|
| Oversigt | Servicestatus | `GET /api/monitoring/alive` |
| Studerende | Elevidentitet | `GET /api/students/me` |
| Studerende | Tilmeld via kode | `POST /api/students/sessions/join` |
| Studerende | Mine tilmeldte sessions | `GET /api/students/sessions` |
| Studerende: sessioner | Én valgt session | `GET /api/students/sessions/{sessionId}` |
| Studerende: hjælp | Opret | `POST /api/students/sessions/{sessionId}/requests` med besked og hjælpeform |
| Studerende: hjælp | Mine anmodninger | `GET /api/students/requests` |
| Studerende: hjælp | Mine anmodninger i valgt session | `GET /api/students/requests?sessionId={sessionId}` |
| Studerende: hjælp | Min køplacering | `GET /api/students/sessions/{sessionId}/queue` |
| Studerende: hjælp | Samtale og status | `GET /api/students/requests/{id}` |
| Studerende: hjælp | Ny besked / genåbn | `POST /api/students/requests/{id}/messages` med `text` |
| Studerende: hjælp | Annuller | `PATCH /api/students/requests/{id}/status` med `action: Cancel` |
| Lærer | Læreridentitet | `GET /api/teachers/me` |
| Lærer: sessioner | Opret session | `POST /api/teachers/sessions` |
| Lærer | Mine sessions | `GET /api/teachers/sessions` |
| Lærer: sessioner | Én valgt session | `GET /api/teachers/sessions/{sessionId}` |
| Lærer: sessioner | Ryd session | `POST /api/teachers/sessions/{sessionId}/clear` |
| Lærer: hjælp | Ventekø og aktiv hjælp | `GET /api/teachers/sessions/{sessionId}/queue` |
| Lærer: hjælp | Reservér næste ledige | `POST /api/teachers/sessions/{sessionId}/queue/take-next` |
| Lærer: hjælp | Samtale og status | `GET /api/teachers/requests/{id}` |
| Lærer: hjælp | Digitalt svar og afslut | `POST /api/teachers/requests/{id}/messages` med `text` |
| Lærer: hjælp | Start, afslut eller send forrest i køen | `PATCH /api/teachers/requests/{id}/status` med `action: Start`, `Complete` eller `Cancel` |

Siden viser metode, URL, sendt JSON, HTTP-status, API-svar og fejl. Hjælpeanmodninger kræver en valgt session. Serveren sætter ID og generation. Se [kø, samtaler og Bruno-forløb](../../../../docs/help-requests.md).

Alle API-kald bruger Angulars dev-proxy i `proxy.conf.json`. Login og logout navigerer til Heimdall. Login fra lærerens hjælpeside vender tilbage til `/teachers/requests`; øvrigt login og logout vender tilbage til `/teachers` på den aktuelle frontend-origin.

## Build

`npm run build` bygger frontenden. Automatiske frontendtests, testkommandoen og Jasmine/Karma-opsætningen er fjernet. Angular-generatorerne er konfigureret til at springe testfiler over.

Dev-proxyen bruges af `npm start`; ved anden hosting skal `/api` routes gennem Heimdall.
