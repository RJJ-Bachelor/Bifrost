# Performancekørsler

**De aktuelle krav er K1: højst ét sekund for oprettelses-POSTens HTTP-svar; K2: højst ti sekunder fra første POST i en frisk kø til første matchende modtagelse hos en af de tre undervisere via SignalR; K3: højst ti sekunder fra svar-POST til ejerens matchende SignalR-besked.** Browsermålinger er valgfri diagnostik. K2 omfatter én kontrolleret første anmodning pr. lektion (`N = Lessons`), ikke senere overgange fra tom kø. Annullerede prøver og K2 med for få prøver er `incomplete`, ikke bestået. K3-proben dækker almindelige digitale svar uden FAQ med præcist request-ID; fysisk hjælp og kanoniske FAQ-broadcasts er diagnostik uden K3-dækning.

## Aktuel smoke med K1–K3

Kørsel `20261105T134738793Z-43538d` brugte de aktuelle måleregler: 10 studerende, tre aktive, én lektion og tre undervisere; separat warm-up med to brugere i 10 sekunder, derefter 10/30/5 sekunders ramp-up/steady-state/ramp-down.

| Krav | N | Maksimum | Resultat |
|---|---:|---:|---|
| K1: Registreringens HTTP-svar, højst 1 sekund | 35 | 482,5974 ms | bestået |
| K2: Første køanmodning → en undervisers SignalR, højst 10 sekunder | 1 | 665,4236 ms | bestået |
| K3: Digitalt svar uden FAQ → ejerens SignalR, højst 10 sekunder | 37 | 1.888,5969 ms | bestået |

Alle kravprøver havde nul fejl, overskridelser og annulleringer. 2.313 HTTP-kald gav nul HTTP-fejl. Der var 13 forbindelser ved maksimum, 13 planlagte lukninger og nul forbindelser til sidst. To øvrige GET-kald blev annulleret af klienten og bevares særskilt. Miljøet var lokal Aspire/Development med Debug-backend, Release-generator og Docker/PostgreSQL/RabbitMQ, `Generalization=Fake`. Kun generatorens ressourcer blev målt; ingen browserklienter.

[Rapport](criteria-smoke-20261105T134738Z/report.html), [rådata](criteria-smoke-20261105T134738Z/raw.json), [kravoversigt](criteria-smoke-20261105T134738Z/summary.json) og [profil/miljø](criteria-smoke-20261105T134738Z/README.md). Det er en bestået **10-brugerprøve**, ikke en godkendelse af 1.000 studerende.

## Historiske snapshots

De tidligere 100-brugerprøver og den fulde kørsel `20261105T093042273Z-320c64` brugte andre måleregler og **afvises som verifikation af K1–K3**. Den nuværende backend er ikke dokumenteret bestået ved den nye standard på 1.285 studerende. Se [rettelser og ny verifikation](performance-improvements.md).

Rapporter, rådata og grafer i de eksisterende snapshotmapper bevares uændret som historik; deres status gælder deres daværende kode, krav og miljø. Nedenstående små opsætningsprøver dokumenterer ikke kapacitet ved 1.000 samtidige studerende. Ingen syntetiske selvtestdata indgår i mapperne.

Miljø: lokal Yggdrasil/Aspire, backend i eksisterende Development-konfiguration, PostgreSQL/RabbitMQ i containere, `Generalization=Fake`, generator bygget i Release med .NET 10.0.14, Windows 10.0.26300 x64, 16 logiske processorer og loopbacknetværk. Repositoryets udgangspunkt var commit `70982f3248efaa83d6589d374cdce370e7af2405` plus de nye performancefiler. Den særskilt tilladte seedtilføjelse blev lavet efter den første prøve.

## Historisk prøve med de tre seedbrugere

Kørsels-ID `20261104T184150616Z-473d53`, 4. november kl. 19:41 dansk tid. VarService oprettede `performance-teacher-1`, `performance-teacher-2` og `performance-teacher-3` ved opstart; alle tre blev derefter valideret som separate undervisere gennem normalt OIDC-login og gatewayens identitets-API. De samme autentificerede HTTP-klienter blev genbrugt gennem warm-up og hovedmåling.

Profil: separat 10-sekunders warm-up med 2 studerende; derefter 10 sekunders ramp-up, 60 sekunders steady-state og 5 sekunders ramp-down. 10 NBomber-studerende, 3 aktive, 3 undervisere og 2 ekstra browserstuderende i én lektion.

- **3.153 afsluttede HTTP-kald, 0 HTTP-fejl**, 10 særskilt registrerede klientannulleringer og 1.120 modtagne SignalR-beskeder.
- Maksimalt **15 faktiske forbindelser**, 0 tilbage efter afslutning. Alle 13 .NET SignalR-tilkoblinger lykkedes; p95 for tilkobling var 475,33 ms.
- Submit → synlig kø: **517,4 og 633,9 ms** i de to browserklienter.
- **2 browser-timeouts** ved venten på synligt undervisersvar efter ca. 30 sekunder; samlet exitkode 1. Begge modtog deres matchende `HelpRequestAnswered`. Tilstand og skærmbilleder viser derefter sessionssiden uden køkort eller samtale, mens det matchende notifikations-ID findes i browserens måledata. Den automatiske svarvisning bestod derfor ikke i disse observationer. Den præcise årsag i frontend er ikke rettet eller endeligt fastslået her.
- Ressourcer blev målt for generatoren (PID 36360), Eir (24276), Gna (5980), gateway (7400), Mimir (37716) og Var (14096), uden utilgængelige procesmålinger.

[Seneste rapport med grafer](seeded-browser-smoke-20261104T184150Z/report.html), [rådata](seeded-browser-smoke-20261104T184150Z/raw.json), [CSV-oversigt](seeded-browser-smoke-20261104T184150Z/summary.csv), [diagnostik og skærmbilleder](seeded-browser-smoke-20261104T184150Z/browser-diagnostics/).

Prøven verificerer opsætningen, de tre nye seedbrugeres reelle login, hjælpearbejdsflows og målepipelinen. Den dokumenterer også en synlig UI-fejl under den lille belastning. Den er ikke en bestået P1-/P3-verifikation ved fuld belastning.

## HTTP/SignalR-prøve

Kørsels-ID `20261104T133925493Z-01403c`, måling startet 14:40:44 dansk tid. Separat warm-up med to studerende i 10 sekunder. Hovedprofil: 10 sekunders ramp-up, 30 sekunders steady-state og 5 sekunders ramp-down; 10 studerende, heraf 3 aktive, og 3 undervisere i én lektion.

- 1.383 afsluttede API-HTTP-kald, 0 HTTP-fejl og 0 øvrige registrerede fejl.
- 471 modtagne SignalR-beskeder; maksimalt 13 faktiske forbindelser, 0 til sidst.
- Alle 10 unikke studentidentiteter blev kontrolleret. Første steady-state-sekund havde endnu kun 12 samlede forbindelser; derefter 13. Det faktiske antal er bevaret i rådata.
- Ingen browserklienter i denne prøve. Ressourcekurven måler kun generatoren.
- Denne tidlige prøve blev kørt før særskilt måling af .NET SignalR-negotiate og før aktivering af server-GC i generatoren. Hovedmålingerne af API-svar og korrelerede notifikationer bruger samme metode som den endelige runner.

| Steady-state-handling | N | p50 ms | p95 ms | p99 ms | Maks. ms | ≥ 1 s | Fejl |
|---|---:|---:|---:|---:|---:|---:|---:|
| Opret anmodning, HTTP | 12 | 175,02 | 268,29 | 268,29 | 268,29 | 0 | 0 |
| Hent studenterkø, HTTP | 361 | 60,36 | 107,41 | 173,99 | 257,77 | 0 | 0 |
| Opret → matchende SignalR | 12 | 3194,93 | 4122,05 | 4122,05 | 4122,05 | 12 | 0 |
| Undervisersvar → studentens SignalR | 11 | 3488,52 | 4465,41 | 4465,41 | 4465,41 | 11 | 0 |

[Grafer og alle operationer](smoke-20261104T133925Z/report.html), [rådata](smoke-20261104T133925Z/raw.json), [CSV-oversigt](smoke-20261104T133925Z/summary.csv).

HTTP-tiderne er korte i denne lille prøve, mens notifikationer tager flere sekunder. Notifikationernes 1-sekundsgrænse er beskrivende; prøven fastslår ikke i sig selv P3's fulde visuelle krav. NBombers iterationstider indeholder tænketid og skal ikke bruges som HTTP-svartid.

## Browserprøve og backendressourcer

Kørsels-ID `20261104T134933543Z-6860d1`, 4. november kl. 14:50 dansk tid. Samme lille profil, men 60 sekunders steady-state, 5 sekunders studentpause og to ekstra Chromium-klienter. Generatoren brugte server-GC. API-processerne blev målt direkte via deres daværende PID'er: Eir 7716, Gna 33412, gateway 24180, Mimir 164 og Var 29124.

- 3.900 afsluttede HTTP-kald, **0 HTTP-fejl**, 2 særskilt registrerede klientannulleringer.
- Maksimalt 15 forbindelser: 10 NBomber-studerende, 3 undervisere og 2 browserstuderende. Sluttal 0.
- To submit → synlig kø-målinger: **315,9 og 317,0 ms**.
- Begge browseres venten på synligt undervisersvar endte i timeout efter ca. 30 sekunder. De to fejl er bevaret som browserfejl; prøvens exitkode var 1.
- Begge browserklienter modtog deres `HelpRequestAnswered` med det matchende request-ID efter ca. 20 sekunders måletid, men ingen efterfølgende request-detail-HTTP-kald blev observeret. Det peger mod frontendens håndtering af afsluttet kø/notifikationer, men årsagen er ikke endeligt fastslået i denne prøve. Frontendkode er ikke ændret.

[Browsergrafer og fejl](browser-smoke-20261104T134933Z/report.html), [rådata](browser-smoke-20261104T134933Z/raw.json), [procesressourcer](browser-smoke-20261104T134933Z/resources.csv). Dette bekræfter indsendelsens synlige feedback i en lille prøve; det verificerer ikke P3 fuldt.

## Oprydning og begrænsninger

Testens sessions blev slettet gennem gatewayen og verificeret som 404/fraværende i undervisernes lister. Alle klientforbindelser blev lukket. Serverens `ConnectionRegistry` er ikke direkte tilgængeligt i den offentlige API.

Den eksisterende backend implementerer ikke fuld sletning af tilhørende hjælpeanmodninger, beskeder, tællere, deltagelser og eventuelle FAQ-/Mimir-data. Sessions-API'ets 404 beviser derfor ikke fuld databasesletning. Dette kræver et isoleret, disponibelt testdatasæt og er ikke ændret af performanceprojektet.

Den lokale Aspire-telemetrieksport ligger i den ignorerede oprindelige run-mappe. Den er ikke kopieret til disse delbare resultater, da infrastrukturens logs kan indeholde følsomme driftsoplysninger.
