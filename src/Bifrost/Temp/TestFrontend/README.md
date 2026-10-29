# Bifrost testfrontend

En Angular-testfrontend til EirService gennem Heimdall. Designet er inspireret af [Aarhus Universitets blå farver og stil](https://www.au.dk/) med indrammede sektioner, tydelige overskriftsbjælker og separate blå og petrolfarvede baggrunde til undervisere og studerende. Layoutet tilpasser sig mobil, tablet og desktop.

Start backendservices og Heimdall på `http://localhost:6004`. Kør `npm start` i denne mappe, og åbn **http://localhost:7003**. Brug `localhost`, så login-cookies deles med Heimdall. Aspire kan også starte frontenden på sin konfigurerede port.

## Sider

| Side | URL | Indhold |
| --- | --- | --- |
| Forside | `/` | Tom forside. En allerede oprettet SignalR-forbindelse bevares. |
| Studerende | `/students` | Tilmelding via kode og liste over tilmeldte sessioner. |
| Studentens session | `/students/sessions/:sessionId` | Køplacering, opret hjælp, egne anmodninger, samtaler og arkiv. |
| Underviser | `/teachers` | Eksisterende lærerlogin, opret session og egne/tilmeldte sessioner med antal i kø. |
| Underviserens session | `/teachers/sessions/:sessionId` | Del sessionskode/link, se køen, tag næste, hjælp, arkivér og ryd session. |

De gamle URL'er `/overview`, `/students/requests`, `/teachers/requests` og `/requests` viderestilles til de tilsvarende hovedsider. Sessionslinks bruger `/students?code=SESSIONSKODE`.

Underviserens sessionskort viser antal ventende anmodninger i den aktuelle generation, også når køen er tom. Tallet kommer fra sessionslisten og opdateres sammen med den. Det svarer til kø-endpointets `waitingCount`: alle `Pending`-anmodninger, herunder reserverede anmodninger og FAQ-spørgsmål. Anmodninger under fysisk behandling eller som afventer FAQ-svar/bekræftelse tælles ikke med.

En session åbnes fra listen. Sessionens ID og aktuelle generation hentes fra API'et. Den valgte samtale gemmes pr. rolle og session i browserfanens session storage, så den kan åbnes igen efter genindlæsning.

På underviserens sessionsside vises et enkelt køkort med antal ventende, den forrestes ventetid og aktuel generation samt **Dagens statistik** med afsluttede fysiske/digitale anmodninger, unikke hjulpne studerende og gennemsnitlig tid fra kø til afslutning. Statistikken gælder den aktuelle generation; gennemsnittet vises som `–`, når der ingen afsluttede anmodninger er. Den fulde underviserkø bruges stadig til anmodningslisterne. Studerende ser et estimat ved formularen, også før de selv har en anmodning i køen.

Køkortets **Tag næste** styres af det lette status-endpoints `unreservedWaitingCount` og `myReservedRequestId`, også når den fulde køliste ikke er indlæst. Knappen er deaktiveret uden ledige anmodninger, under indlæsning/handlinger, ved en forældet generation og når underviseren allerede har en reserveret eller aktiv anmodning. **Fortsæt med dit spørgsmål** åbner den eksisterende anmodning via dens ID. `waitingCount` omfatter alle ventende anmodninger, mens `unreservedWaitingCount` kun omfatter de ventende, som ingen underviser har reserveret.

Dagens statistik hører til Insights-modulet, som henter afsluttede anmodninger gennem en intern Contracts-query til HelpRequests. Kø og køstatus hører til HelpRequests-modulet.

**Forventet ventetid** (`estimatedWaitMinutes`) er gennemsnitlige minutter pr. køplads × antal foran, rundet op til hele minutter. Køens tempo måles som `(sidste StartedAt - første StartedAt) / (antal starter - 1)` i den aktuelle generation, samlet for alle undervisere. Digital hjælp starter ved reservation; fysisk hjælp ved Start. Annullerede anmodninger og `CompletedByFaq` udelades; selve FAQ-opgaven tæller med. Før indmelding tælles alle `Pending`-anmodninger; for en elev i køen bruges køplacering minus én. Reserverede digitale anmodninger er fortsat `Pending`; aktive lægges ikke oveni. Der kræves mindst to starter og et positivt tidsrum mellem første og sidste. Ellers er estimatet `null`, også ved tom kø, og frontenden viser “Ventetid kan endnu ikke beregnes”. Med kendt tempo og ingen foran er estimatet `0`. Starter ved minut 2, 5 og 9 giver 3,5 minutter pr. plads og dermed 14 minutter med fire foran. Estimatet er et overslag, ikke en nedtælling; pauser mellem starter indgår.

**Gennemsnitlig tid** (`averageWaitMinutes`) i Dagens statistik er fortsat gennemsnittet af `ClosedAt - QueuedAt` for afsluttede anmodninger: både ventetid og hjælp. Det beskriver tidligere forløb og bruges ikke til at beregne køens tempo. Se [forklaring og beregninger](../../../../docs/QUEUE_WAIT_TIMES.md).

| GET-endpoint | Brug |
| --- | --- |
| `/api/teachers/sessions/{sessionId}/statistics` | Dagens statistik i den aktuelle generation. |
| `/api/students/sessions/{sessionId}/queue/status` | Antal ventende og estimeret ventetid for en ny anmodning. |
| `/api/teachers/sessions/{sessionId}/queue/status` | `waitingCount`, `unreservedWaitingCount`, `frontWaitMinutes`, `generation` og `myReservedRequestId`. |

Samtalen viser underviserens ID (eget navn hvis kendt), køtid, starttid og afslutningstid samt om hjælpen blev afsluttet fysisk, med et digitalt svar, med et accepteret FAQ-svar eller annulleret. Ved FAQ-svar kommer underviseren fra svarets forfatter. Underviserens ID vises også på svar og i arkiverede samtaler.

Afsluttede anmodninger har status `CompletedByFaq`, `CompletedByDigital` eller `CompletedByPhysical`. Alle tre vises som afsluttede, tillader den studerende at sende en opfølgning og kan ikke annulleres igen. FAQ-opgaven selv afsluttes digitalt; den studerendes accept af et fælles FAQ-svar giver `CompletedByFaq`.

## Hjælpeflows

- **Studerende:** Vælg fysisk eller digital hjælp, og stil dit spørgsmål. Se din køplacering og samtale. Annuller før fysisk hjælp starter. Send opfølgende beskeder i en ventende, ureserveret eller afsluttet samtale; den genåbnes bagest i køen.
- **Underviser:** Tag næste anmodning fra køkortet. Der kan højst være én reserveret (`Pending`) eller aktiv (`Active`) anmodning pr. underviser i samme session og generation. Gentaget `take-next` returnerer den eksisterende anmodning; **Fortsæt med dit spørgsmål** åbner den. Ved fysisk hjælp bruges **Start fysisk hjælp** og **Afslut fysisk hjælp**. Ved digital hjælp afslutter **Send svar og afslut** anmodningen. **Send forrest i køen** frigiver reservationen og returnerer anmodningen.
- **Ryd session:** Findes under sessionindstillinger og kræver bekræftelse. De studerende forbliver tilmeldt. Gamle samtaler kan læses, men handlinger kræver sessionens aktuelle generation.
- **Arkivér generation:** Underviseren vælger handlingen under sessionindstillinger og bekræfter. FAQ og personlige samtaler gemmes i arkivet, den aktuelle kø tømmes, og generationen stiger med én. Alle deltagere forbliver tilmeldt. Den nye arkivdetalje åbnes, og køen opdateres. **Ryd session uden arkiv** er stadig tilgængelig som en separat handling.
- **Arkiverede generationer:** Begge roller kan vælge en generation under sessionens kø og læse FAQ og samtaler. Underviseren ser alle gemte FAQ-spørgsmål og sessionens personlige samtaler. Den studerende ser besvarede FAQ-spørgsmål og sine egne samtaler. Arkivet er kun til læsning.
- Numre som **BF-002** identificerer en anmodning; køplaceringen kan ændre sig.

Brug en separat browserprofil til hver studerende. Faner i samme profil deler den eksisterende studenter-cookie. Lærerlogin og logout bruger fortsat Heimdall og VarService.

## SignalR og opdateringer

Cirklen øverst til højre viser SignalR-forbindelsens tilstand. Grøn betyder forbundet. Rød betyder afbrudt, opkobling eller genforbindelse. Hold markøren over cirklen for en tekststatus. Status følger den aktuelle rolle og identitet, også ved fejl og genforbindelse.

SignalR-forbindelsen tilhører hele appen. Efter besøg på student- eller undervisersiden bevares den ved navigation, også på den tomme forside, hvor notifikationer stadig logges. Den senest autentificerede rolle bruges; et rolleskift skifter forbindelsen. Udløbet login lukker den relevante forbindelse.

Siden henter aktuelle data ved første indlæsning, relevante SignalR-notifikationer, genforbindelse og egne handlinger. Der bruges ingen periodisk polling af kø- eller sessionsdata. SignalR kan anvende Long Polling som transportfallback, hvis WebSocket ikke er tilgængelig; dette henter notifikationer og er ikke polling af kø-endpointet.

Notifikationens `change` beskriver, hvilke data der er ændret. Frontenden genhenter kun berørte, synlige data. På sessionsoversigten opdaterer underviseren kun listen ved mulige ændringer i `waitingCount` eller sessionsmetadata. Den studerendes sessionsoversigt opdateres ved rydning, arkivering og sletning. På en åben sessionsside hentes den skjulte sessionsliste ikke. Almindelige køændringer henter køen; metadata og arkivlisten hentes ikke igen ved hver anmodning. En valgt undervisersamtale genbruges fra køens komplette requestdata, når den stadig er ventende eller aktiv.

Køstatus hentes sammen med køen. Dagens statistik opdateres ved afslutning, svar, FAQ-accept og genåbning med en ny studentbesked samt ved fuld genindlæsning eller ny generation.

Reservation, accept af FAQ-svar og annullering under FAQ-forløbet varsler kun ejeren og underviserne. Afsluttet fysisk hjælp varsler desuden alle sessionens deltagere, fordi både køens ventetidsestimat og formularens estimat for en ny anmodning kan ændres.

| `change` | Data på en åben sessionsside |
|---|---|
| `Created`, `StudentMessageAdded`, `Started`, `Completed`, `Answered`, `Cancelled`, `ReturnedToQueue`, `FaqRejected` | Køen. Egen anmodningsliste og valgt samtale hentes kun, når notifikationen vedrører den studerendes anmodning. |
| `Reserved` | Underviserens kø og eventuelt den valgte samtale. Den studerendes køtal og placering er uændrede; ejeren genhenter kun den valgte samtale. |
| `FaqAccepted`, `FaqCancelled` | Ejerens kø og anmodningsliste samt en berørt valgt samtale. Øvrige studerende får ingen hændelse. |
| `FaqCreated`, `FaqLinked` | Køen og underviserens FAQ-liste; ejerens berørte anmodning opdateres også. |
| `FaqAnswered`, `FaqSuggested` | Køen og FAQ-listen samt berørte egne anmodninger og samtaler. |
| `ArchivedFaqAnswered` | Arkivlisten og den åbne arkivdetalje. En igangværende arkivlæsning erstattes af en ny, så et forsinket svar ikke overses. |
| `Cleared`, `Archived`, `Deleted`, nyere generation eller ukendt/ældre beskedformat | Alle synlige data for at opdatere sessionskonteksten sikkert. |

Request-ID og status medfølger kun til ejeren og autoriserede undervisere. Andre studerende modtager den generelle årsag uden private requestdata. Egne mutationssvar opdaterer samtalen og den studerendes anmodningsoversigt direkte; nødvendige supplerende læsninger samles med notifikationer i samme opdateringskø. Et asynkront generaliseringskald afventer hændelsen frem for at genhente uændrede data efter `202 Accepted`.

Flere tætliggende hændelser samles efter højst 150 ms, også ved vedvarende trafik. Ressourcer, der ændres under igangværende kald, flettes til én efterfølgende opdatering. Der bruges ingen tidsbaseret undertrykkelse af hændelser. Kladder bevares. API-kald og sidens abonnementer stoppes ved navigation; SignalR-forbindelsen skiftes ved rolleskift eller ændret identitet og stoppes ved udløbet identitet eller lukning af appen.

Alle modtagne notifikationer logges med `console.info` i browserens udviklerkonsol uden at åbne en modal, også almindelige køændringer med `SessionChanged` og `RealtimeOnly`. Filtrér konsollen på `[SignalR]` for at se dem. Loggen indeholder en overskrift ud fra `eventType` og det fulde notifikationsobjekt med blandt andet besked, leveringstype, session, generation, status og anmodning. `deliveryType` angiver leveringskanalen: `RealtimeOnly` leveres kun via SignalR, mens `RealtimeOrPush` også tillader push ved behov. Begge typer behandles fortsat af sidens abonnementer, så sessioner, kø og samtaler opdateres som før. Genlevering med samme notifikations-ID behandles og logges kun én gang på samme forbindelse.

| `eventType` | Modtager og årsag |
|---|---|
| `HelpRequestCreated` | Sessionens undervisere, når en ny anmodning starter en tom kø |
| `StudentMessageAdded` | Sessionens undervisere, når en opfølgning starter en tom kø |
| `HelpRequestStarted` | Eleven, når underviseren starter fysisk hjælp |
| `HelpRequestAnswered` | Eleven, når underviseren besvarer en almindelig anmodning |
| `FaqCreated` | Alle sessionens undervisere samt de studerende, hvis anmodninger kobles på generaliseringen |
| `FaqAnswered` | Hver tilknyttet elev, når det fælles FAQ-svar er klar til accept eller afvisning |
| `SessionChanged` | Øvrige deltagere og undervisere opdaterer sessionsdata via `RealtimeOnly`; hændelsen logges i konsollen |

Ældre eller ukendte eventtyper logges som en almindelig notifikation. Annullerede anmodninger får ikke nye FAQ-svar.

Eirs HelpRequests-outbox og Gnas leverings-outbox kontrolleres hvert 500 ms. Gnas `NotificationOutboxDispatcher` læser leveringsopgaver direkte fra databasen og kalder `NotificationDispatcher`, som sender via SignalR eller anvender den tilladte push-fallback. Der er ingen intern RabbitMQ-runde fra Gna til sig selv. Notifikation og leveringsopgave gemmes stadig atomisk, og allerede behandlede opgaver genafsendes ikke. En utilgængelig klient afslutter leveringsforsøget; ved genforbindelse henter frontenden den aktuelle tilstand. Uventede fejl i outboxbehandlingen genforsøges. Dette er backendens outboxpolling, mens kødata i frontenden fortsat opdateres ved hændelser. Web Push-afsendelsen er endnu ikke implementeret.

API-kald bruger elevens cookie eller lærerens login med `withCredentials`. Udviklingsproxyen i `proxy.conf.json` videresender `/api` og WebSockets til Heimdall. Se [Gna-notifikationer](../../../../docs/gna-notifications.md).

## Build

`npm run build` bygger frontenden. Angulars template- og TypeScript-kontrol indgår i buildet. `npm test` kontrollerer notifikationernes eventtyper og leveringstype, navigation, sessionsfiltrering, hændelsesstyret opdatering og genforbindelse, FAQ-svar og arkivering med Node-testværktøjet.

Arkivering bruger `POST /api/teachers/sessions/{sessionId}/archive` med `{ expectedGeneration }`. Arkivlisten hentes fra `GET /api/{teachers|students}/sessions/{sessionId}/archives`, og en detalje fra samme sti med `/{archiveId}`. Listen opdateres sammen med sessionen; en arkivdetalje hentes, når den vælges. Brug **Opdatér arkiv**, hvis et FAQ-svar stadig er på vej gennem beskedkøen. API'et håndhæver medlemskab og filtrerer den studerendes samtaler.

## Generalisering og FAQ

Underviserkøen viser generaliserede FAQ-spørgsmål foran almindelige anmodninger. Tag opgaven og svar på samme måde som ved digital hjælp. De tilknyttede studerende får svaret i deres samtale og kan acceptere eller afvise; afvisning bevarer deres køplads. Begge roller kan se besvarede FAQ'er i sessionens fælles oversigt.

When answering a FAQ task, the teacher can edit the FAQ question in the same form as the answer. The question and answer are submitted together to `POST /api/teachers/requests/{id}/messages` as `{ text, question }`. Ordinary answers still use only `{ text }`.

The teacher can also select **Tilføj FAQ** under the session's **Spørgsmål og svar** and save a question of their own with its answer immediately. This uses `POST /api/teachers/sessions/{sessionId}/faqs` with `{ question, answer }`. The FAQ belongs to the session's current generation and appears immediately in the list. It creates no queue task or student conversation; `sourceCount` is `0`, and the UI displays **Oprettet af en underviser**. It is included when the generation is archived. Forms reset when the session or generation changes.

Eir sender kun det oprindelige spørgsmål fra en ny HelpRequest til Mimir, både ved fysisk og digital hjælp. Opfølgende beskeder og genåbning sender ikke nye spørgsmål til generalisering. Ubesvarede, ventende og ureserverede anmodninger af begge typer kobles på FAQ'en og får FAQ-spørgsmålet, svaret, accept/afvisning og en `FaqAnswered`-notifikation via Gna. Accept afslutter anmodningen. Afvisning bevarer den oprindelige køtid og hjælpetype, så en fysisk anmodning fortsat venter på fysisk hjælp fra underviseren. De normale beskeder om fysisk hjælp, eksempelvis når underviseren starter hjælpen, bevares.

Allerede besvarede anmodninger forbliver afsluttede, selv om deres ID indgår i en generalisering. De får hverken en ny FAQ-notifikation eller krav om accept/afvisning. Eleven kan læse svaret under sessionens **Spørgsmål og svar**; en afsluttet samtale har også **Se fælles FAQ-svar**. En opfølgning efter et undervisersvar eller et tidligere FAQ-svar bliver heller ikke overtaget af en forsinket generalisering af originalspørgsmålet. En afvist FAQ uden en ny opfølgning efterlader stadig originalspørgsmålet åbent for hjælp.

På studentsiden vises **FAQ-svar klar** over køplaceringen, når en underviser har svaret. Vælg **Se svar og acceptér/afvis** for at åbne din oprindelige anmodning. Spørgsmålet, svaret og knapperne **Acceptér FAQ-svar** og **Afvis FAQ-svar – tilbage i køen** står øverst i samtalen. Du kan kun svare på dine egne anmodninger i den aktuelle generation. Accept afslutter anmodningen; afvisning sender den tilbage i køen med den oprindelige køplads. Indtil underviseren har svaret, er status **Afventer fælles FAQ-svar**, og samtalen forklarer, hvornår knapperne bliver tilgængelige.

Efter afvisning vises det afviste FAQ-spørgsmål og FAQ-svar ikke længere i den oprindelige samtale eller underviserens kø. Elevens egne beskeder og almindelige undervisersvar bevares. Den fælles FAQ kan fortsat læses under **Spørgsmål og svar**, og accepterede FAQ-svar bevares i samtalen. API'et beholder beskedernes oprindelige sekvensnumre, så skjulte FAQ-beskeder kan give spring i numrene.

Brug **Test af Mimir-generalisering** i underviserens session til at sende de to viste, ledige spørgsmål med **Generaliser de to viste spørgsmål**. Knappen bruger Mimirs development-endpoint gennem Heimdall og viser en afventer-status, indtil FAQ'en er synlig. En gentagelse efter en HTTP-fejl genbruger generaliserings-ID'et. Du kan også kopiere JSON-dataene til Bruno-kaldet **MimirService → Generalize own help requests**. Kaldet bruger `POST /api/teachers/development/generalizations/from-help-requests` gennem Heimdall (teacher-login) og videresender til Mimir `POST /api/test/generalizations/from-help-requests`. Mimir sender teksten “Dette er et generaliseret spørgsmål.” med dine IDs gennem den normale outbox og RabbitMQ. Du kan også oprette anmodningerne i Bruno; seed-demoen er valgfri.

Brug mindst to ventende, ureserverede anmodninger fra samme session og aktuelle generation. `202 Accepted` betyder, at eventet er gemt til afsendelse. Når Eir har behandlet eventet, kan underviseren tage FAQ'en og svare; de tilknyttede studerende accepterer eller afviser svaret. Se [Bruno-vejledningen](../../../../scripts/Bifrost-JPF/Development/README.md#generalisering-af-egne-helprequests) for forløbet. Mimir udfører endnu ikke automatisk gruppering.
