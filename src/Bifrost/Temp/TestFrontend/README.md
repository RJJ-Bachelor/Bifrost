# Bifrost testfrontend

En enkel Angular-frontend til EirService gennem Heimdall. Designet er inspireret af [Aarhus Universitets blå farver og stil](https://www.au.dk/).

Start backendservices og Heimdall på `http://localhost:6004`. Kør `npm start` i denne mappe, og åbn **http://localhost:7003**. Brug `localhost`, så login-cookies deles med Heimdall. Aspire kan også starte frontenden på sin konfigurerede port.

## Sider

| Side | URL | Indhold |
| --- | --- | --- |
| Forside | `/` | Tom forside. En allerede oprettet SignalR-forbindelse bevares. |
| Studerende | `/students` | Tilmelding via kode og liste over tilmeldte sessioner. |
| Studentens session | `/students/sessions/:sessionId` | Køplacering, opret hjælp, egne anmodninger, samtaler og arkiv. |
| Underviser | `/teachers` | Eksisterende lærerlogin, opret session og egne sessioner. |
| Underviserens session | `/teachers/sessions/:sessionId` | Del sessionskode/link, se køen, tag næste, hjælp, arkivér og ryd session. |

De gamle URL'er `/overview`, `/students/requests`, `/teachers/requests` og `/requests` viderestilles til de tilsvarende hovedsider. Sessionslinks bruger `/students?code=SESSIONSKODE`.

En session åbnes fra listen. Sessionens ID og aktuelle generation hentes fra API'et. Den valgte samtale gemmes pr. rolle og session i browserfanens session storage, så den kan åbnes igen efter genindlæsning.

## Hjælpeflows

- **Studerende:** Vælg fysisk eller digital hjælp, og stil dit spørgsmål. Se din køplacering og samtale. Annuller før fysisk hjælp starter. Send opfølgende beskeder i en ventende, ureserveret eller afsluttet samtale; den genåbnes bagest i køen.
- **Underviser:** Tag næste anmodning. Ved fysisk hjælp bruges **Start fysisk hjælp** og **Afslut fysisk hjælp**. Ved digital hjælp afslutter **Send svar og afslut** anmodningen. **Send forrest i køen** frigiver reservationen og returnerer anmodningen.
- **Ryd session:** Findes under sessionindstillinger og kræver bekræftelse. De studerende forbliver tilmeldt. Gamle samtaler kan læses, men handlinger kræver sessionens aktuelle generation.
- **Arkivér generation:** Underviseren vælger handlingen under sessionindstillinger og bekræfter. FAQ og personlige samtaler gemmes i arkivet, den aktuelle kø tømmes, og generationen stiger med én. Alle deltagere forbliver tilmeldt. Den nye arkivdetalje åbnes, og køen opdateres. **Ryd session uden arkiv** er stadig tilgængelig som en separat handling.
- **Arkiverede generationer:** Begge roller kan vælge en generation under sessionens kø og læse FAQ og samtaler. Underviseren ser alle gemte FAQ-spørgsmål og sessionens personlige samtaler. Den studerende ser besvarede FAQ-spørgsmål og sine egne samtaler. Arkivet er kun til læsning.
- Numre som **BF-002** identificerer en anmodning; køplaceringen kan ændre sig.

Brug en separat browserprofil til hver studerende. Faner i samme profil deler den eksisterende studenter-cookie. Lærerlogin og logout bruger fortsat Heimdall og VarService.

## SignalR og opdateringer

SignalR-forbindelsen tilhører hele appen. Efter besøg på student- eller undervisersiden bevares den ved navigation, også på den tomme forside, hvor notifikationer stadig vises. Den senest autentificerede rolle bruges; et rolleskift skifter forbindelsen. Udløbet login lukker den relevante forbindelse.

Siden henter aktuelle data ved relevante notifikationer, genforbindelse og egne handlinger. Notifikationer fra andre sessioner eller tidligere generationer opdaterer ikke den åbne samtale. Synlige studentsider opdateres også hvert 10. sekund og undervisersider hvert 2. sekund, fordi ikke alle køændringer udsender notifikationer. Opdatering af data bevarer kladder. Sidens timers og API-kald stoppes ved navigation; SignalR-forbindelsen skiftes ved rolleskift eller ændret identitet og stoppes ved udløbet identitet eller lukning af appen.

Notifikationer viser en overskrift ud fra `eventType`, beskeden og dens session, generation, status og anmodning. **Åbn samtalen** åbner den rette elev- eller underviserside og den konkrete HelpRequest. Flere notifikationer vises én ad gangen. Genlevering med samme notifikations-ID vises kun én gang på samme forbindelse.

| `eventType` | Modtager og årsag |
|---|---|
| `HelpRequestCreated` | Sessionens undervisere, når en ny anmodning starter en tom kø |
| `StudentMessageAdded` | Sessionens undervisere, når en opfølgning starter en tom kø |
| `HelpRequestStarted` | Eleven, når underviseren starter fysisk hjælp |
| `HelpRequestAnswered` | Eleven, når underviseren besvarer en almindelig anmodning |
| `FaqCreated` | Alle sessionens undervisere, når en generalisering bliver til en FAQ |
| `FaqAnswered` | Hver tilknyttet elev, når det fælles FAQ-svar er klar til accept eller afvisning |

Ældre eller ukendte eventtyper vises som en almindelig notifikation. Annullerede anmodninger får ikke nye FAQ-svar.

Eirs HelpRequests-outbox og Gnas leverings-outbox kontrolleres hvert 500 ms. Tidligere brugte begge 5 sekunder, hvilket kunne lægge næsten 10 sekunders ventetid oven i transport og behandling. Øvrige outboxes beholder deres eksisterende interval.

API-kald bruger elevens cookie eller lærerens login med `withCredentials`. Udviklingsproxyen i `proxy.conf.json` videresender `/api` og WebSockets til Heimdall. Se [Gna-notifikationer](../../../../docs/gna-notifications.md).

## Build

`npm run build` bygger frontenden. Angulars template- og TypeScript-kontrol indgår i buildet. `npm test` kontrollerer notifikationernes eventtyper, navigation, sessionsfiltrering, FAQ-svar og arkivering med Node-testværktøjet.

Arkivering bruger `POST /api/teachers/sessions/{sessionId}/archive` med `{ expectedGeneration }`. Arkivlisten hentes fra `GET /api/{teachers|students}/sessions/{sessionId}/archives`, og en detalje fra samme sti med `/{archiveId}`. Listen opdateres sammen med sessionen; en arkivdetalje hentes, når den vælges. Brug **Opdatér arkiv**, hvis et FAQ-svar stadig er på vej gennem beskedkøen. API'et håndhæver medlemskab og filtrerer den studerendes samtaler.

## Generalisering og FAQ

Underviserkøen viser generaliserede FAQ-spørgsmål foran almindelige anmodninger. Tag opgaven og svar på samme måde som ved digital hjælp. De tilknyttede studerende får svaret i deres samtale og kan acceptere eller afvise; afvisning bevarer deres køplads. Begge roller kan se besvarede FAQ'er i sessionens fælles oversigt.

Eir sender kun det oprindelige spørgsmål fra en ny HelpRequest til Mimir, både ved fysisk og digital hjælp. Opfølgende beskeder og genåbning sender ikke nye spørgsmål til generalisering. Ubesvarede, ventende og ureserverede anmodninger af begge typer kobles på FAQ'en og får FAQ-spørgsmålet, svaret, accept/afvisning og en `FaqAnswered`-notifikation via Gna. Accept afslutter anmodningen. Afvisning bevarer den oprindelige køtid og hjælpetype, så en fysisk anmodning fortsat venter på fysisk hjælp fra underviseren. De normale beskeder om fysisk hjælp, eksempelvis når underviseren starter hjælpen, bevares.

Allerede besvarede anmodninger forbliver afsluttede, selv om deres ID indgår i en generalisering. De får hverken en ny FAQ-notifikation eller krav om accept/afvisning. Eleven kan læse svaret under sessionens **Spørgsmål og svar**; en afsluttet samtale har også **Se fælles FAQ-svar**. En opfølgning efter et undervisersvar eller et tidligere FAQ-svar bliver heller ikke overtaget af en forsinket generalisering af originalspørgsmålet. En afvist FAQ uden en ny opfølgning efterlader stadig originalspørgsmålet åbent for hjælp.

På studentsiden vises **FAQ-svar klar** over køplaceringen, når en underviser har svaret. Vælg **Se svar og acceptér/afvis** for at åbne din oprindelige anmodning. Spørgsmålet, svaret og knapperne **Acceptér FAQ-svar** og **Afvis FAQ-svar – tilbage i køen** står øverst i samtalen. Du kan kun svare på dine egne anmodninger i den aktuelle generation. Accept afslutter anmodningen; afvisning sender den tilbage i køen med den oprindelige køplads. Indtil underviseren har svaret, er status **Afventer fælles FAQ-svar**, og samtalen forklarer, hvornår knapperne bliver tilgængelige.

Efter afvisning vises det afviste FAQ-spørgsmål og FAQ-svar ikke længere i den oprindelige samtale eller underviserens kø. Elevens egne beskeder og almindelige undervisersvar bevares. Den fælles FAQ kan fortsat læses under **Spørgsmål og svar**, og accepterede FAQ-svar bevares i samtalen. API'et beholder beskedernes oprindelige sekvensnumre, så skjulte FAQ-beskeder kan give spring i numrene.

Brug **Test af Mimir-generalisering** i underviserens session til at sende de to viste, ledige spørgsmål med **Generaliser de to viste spørgsmål**. Knappen bruger Mimirs development-endpoint gennem Heimdall og viser en afventer-status, indtil FAQ'en er synlig. En gentagelse efter en HTTP-fejl genbruger generaliserings-ID'et. Du kan også kopiere JSON-dataene til Bruno-kaldet **MimirService → Generalize own help requests**. Kaldet bruger `POST /api/teachers/development/generalizations/from-help-requests` gennem Heimdall (teacher-login) og videresender til Mimir `POST /api/test/generalizations/from-help-requests`. Mimir sender teksten “Dette er et generaliseret spørgsmål.” med dine IDs gennem den normale outbox og RabbitMQ. Du kan også oprette anmodningerne i Bruno; seed-demoen er valgfri.

Brug mindst to ventende, ureserverede anmodninger fra samme session og aktuelle generation. `202 Accepted` betyder, at eventet er gemt til afsendelse. Når Eir har behandlet eventet, kan underviseren tage FAQ'en og svare; de tilknyttede studerende accepterer eller afviser svaret. Se [Bruno-vejledningen](../../../../scripts/Bifrost-JPF/Development/README.md#generalisering-af-egne-helprequests) for forløbet. Mimir udfører endnu ikke automatisk gruppering.
