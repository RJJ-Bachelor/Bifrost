# Bifrost testfrontend

En enkel Angular-frontend til EirService gennem Heimdall. Designet er inspireret af [Aarhus Universitets blå farver og stil](https://www.au.dk/).

Start backendservices og Heimdall på `http://localhost:6004`. Kør `npm start` i denne mappe, og åbn **http://localhost:7003**. Brug `localhost`, så login-cookies deles med Heimdall. Aspire kan også starte frontenden på sin konfigurerede port.

## Sider

| Side | URL | Indhold |
| --- | --- | --- |
| Forside | `/` | Tom forside. En allerede oprettet SignalR-forbindelse bevares. |
| Studerende | `/students` | Tilmelding via kode og liste over tilmeldte sessioner. |
| Studentens session | `/students/sessions/:sessionId` | Køplacering, opret hjælp, egne anmodninger og samtaler. |
| Underviser | `/teachers` | Eksisterende lærerlogin, opret session og egne sessioner. |
| Underviserens session | `/teachers/sessions/:sessionId` | Del sessionskode/link, se køen, tag næste, hjælp og ryd session. |

De gamle URL'er `/overview`, `/students/requests`, `/teachers/requests` og `/requests` viderestilles til de tilsvarende hovedsider. Sessionslinks bruger `/students?code=SESSIONSKODE`.

En session åbnes fra listen. Sessionens ID og aktuelle generation hentes fra API'et. Den valgte samtale gemmes pr. rolle og session i browserfanens session storage, så den kan åbnes igen efter genindlæsning.

## Hjælpeflows

- **Studerende:** Vælg fysisk eller digital hjælp, og stil dit spørgsmål. Se din køplacering og samtale. Annuller før fysisk hjælp starter. Send opfølgende beskeder i en ventende, ureserveret eller afsluttet samtale; den genåbnes bagest i køen.
- **Underviser:** Tag næste anmodning. Ved fysisk hjælp bruges **Start fysisk hjælp** og **Afslut fysisk hjælp**. Ved digital hjælp afslutter **Send svar og afslut** anmodningen. **Send forrest i køen** frigiver reservationen og returnerer anmodningen.
- **Ryd session:** Findes under sessionindstillinger og kræver bekræftelse. De studerende forbliver tilmeldt. Gamle samtaler kan læses, men handlinger kræver sessionens aktuelle generation.
- Numre som **BF-002** identificerer en anmodning; køplaceringen kan ændre sig.

Brug en separat browserprofil til hver studerende. Faner i samme profil deler den eksisterende studenter-cookie. Lærerlogin og logout bruger fortsat Heimdall og VarService.

## SignalR og opdateringer

SignalR-forbindelsen tilhører hele appen. Efter besøg på student- eller undervisersiden bevares den ved navigation, også på den tomme forside, hvor notifikationer stadig vises. Den senest autentificerede rolle bruges; et rolleskift skifter forbindelsen. Udløbet login lukker den relevante forbindelse.

Siden henter aktuelle data ved notifikationer, genforbindelse og egne handlinger. Hvert 10. sekund hentes data også automatisk på synlige studentsider og undervisersider, fordi ikke alle køændringer udsender notifikationer. Opdatering af data bevarer kladder. Sidens timers og API-kald stoppes ved navigation; SignalR stoppes først ved rolleskift, udløbet identitet eller lukning af appen.

Eirs HelpRequests-outbox og Gnas leverings-outbox kontrolleres hvert 500 ms. Tidligere brugte begge 5 sekunder, hvilket kunne lægge næsten 10 sekunders ventetid oven i transport og behandling. Øvrige outboxes beholder deres eksisterende interval.

API-kald bruger elevens cookie eller lærerens login med `withCredentials`. Udviklingsproxyen i `proxy.conf.json` videresender `/api` og WebSockets til Heimdall. Se [Gna-notifikationer](../../../../docs/gna-notifications.md).

## Build

`npm run build` bygger frontenden. Angulars template- og TypeScript-kontrol indgår i buildet. Projektet har ingen Jasmine/Karma-opsætning.

## Generalisering og FAQ

Underviserkøen viser generaliserede FAQ-spørgsmål foran almindelige anmodninger. Tag opgaven og svar på samme måde som ved digital hjælp. De tilknyttede studerende får svaret i deres samtale og kan acceptere eller afvise; afvisning bevarer deres køplads. Begge roller kan se besvarede FAQ'er i sessionens fælles oversigt.

Brug **Test af Mimir-generalisering** i underviserens session til at kopiere JSON-data fra de første to ledige spørgsmål. Indsæt dem i Bruno-kaldet **MimirService → Generalize own help requests**. Kaldet bruger `POST /api/teachers/development/generalizations/from-help-requests` gennem Heimdall (teacher-login) og videresender til Mimir `POST /api/test/generalizations/from-help-requests`. Mimir sender teksten “Dette er et generaliseret spørgsmål.” med dine IDs gennem den normale outbox og RabbitMQ. Du kan også oprette anmodningerne i Bruno; seed-demoen er valgfri.

Brug mindst to ventende, ureserverede anmodninger fra samme session og aktuelle generation. `202 Accepted` betyder, at eventet er gemt til afsendelse. Når Eir har behandlet eventet, kan underviseren tage FAQ'en og svare; de tilknyttede studerende accepterer eller afviser svaret. Se [Bruno-vejledningen](../../../../scripts/Bifrost-JPF/Development/README.md#generalisering-af-egne-helprequests) for forløbet. Mimir udfører endnu ikke automatisk gruppering.
