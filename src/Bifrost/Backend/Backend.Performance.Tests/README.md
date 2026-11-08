# Backend.Performance.Tests

Selvstændigt .NET 10/NBomber-projekt med EirService-DTO'er og virkelige gatewayflows. Projektet køres direkte med `--project`. De aktuelle krav er K1–K3 nedenfor; tidligere rapporter er historik og verificerer ikke disse krav. Se [rettelser og validering](Results/performance-improvements.md) og [historiske resultater](Results/README.md).

Den [aktuelle smoke med 10 studerende](Results/criteria-smoke-20261105T134738Z/README.md) bestod K1–K3. Kapacitet ved 1.285 studerende er endnu ikke dokumenteret efter disse måleregler.

## Start

Start Yggdrasil/Aspire i Development, og vent på PostgreSQL, RabbitMQ, VarService, Eir, Gna og gatewayen. Kør fra repository-roden:

```powershell
# Første gang: Release-build, Chromium til OIDC-login og offline selvtest.
.\scripts\Test\Perfomance\setup.cmd
# Lille prøve; gennemgå rapporten før fuld belastning.
.\scripts\Test\Perfomance\smoke.cmd -OpenReport
# 1.285 studerende og 30 undervisere (tre pr. session); cirka 17 minutter plus klargøring.
.\scripts\Test\Perfomance\full.cmd -OpenReport
```

Scriptsene bruger `performance-teacher-1` til `performance-teacher-30`. I Development opretter VarService automatisk alle 30 konti med `Pass123$`, Teacher-rolle og de nødvendige claims. Ved lokal gateway bruges derfor standardnavnene uden prompt. Angivne passwordmiljøvariabler overstyrer standarden; brugerdefinerede konti og eksterne gateways kræver egne credentials. [Scriptguiden](../../../../scripts/Test/Perfomance/README.md) beskriver øvrige parametre og brugerdefinerede credentials. Direkte `dotnet`-kørsel kræver stadig miljøvariablerne nedenfor. Hvis warm-up fejler, indeholder rapportens `Failure` nu actor og konkret exception i stedet for kun et samlet antal.

Direkte kørsel efter setup:

```powershell
$project = 'src/Bifrost/Backend/Backend.Performance.Tests/Backend.Performance.Tests.csproj'
$env:BIFROST_PERF_GATEWAY_URL = 'http://localhost:6004'
# Credentials skal være sat i terminalens miljø, se nedenfor.
dotnet run --project $project -c Release --no-build -- --check-teachers
dotnet run --project $project -c Release --no-build -- --smoke
dotnet run --project $project -c Release --no-build
```

Gatewayens faktiske URL findes i Aspire. `--check` kontrollerer kun studentcookie; `--check-teachers` kontrollerer også alle 30 underviseres login, identiteter og rettigheder uden belastning. `--self-test` er en offline kontrol med syntetiske data, som ikke er performanceresultater. Opstartsfejl vises i rapporten med besked om, at målefasen ikke begyndte.

Ved fuld lokal belastning konfigurerer AppHost EirService med en delt PostgreSQL-forbindelsespulje på 80 forbindelser (`Database__MaximumPoolSize`). Det er nødvendigt, fordi køgenindlæsninger fra 1.285 samtidige SignalR-klienter ellers kan vente på standardpuljen på 40 forbindelser og forvrænge K1–K3-målingerne. Ved et andet miljø skal PostgreSQL's samlede forbindelsesgrænse dimensioneres tilsvarende.

## Krav og præcis måling

| Krav | Måling | Grænse |
|---|---|---|
| K1: Registrér hjælpeanmodning | Afsendelse af `POST /api/students/sessions/{id}/requests` → færdigt HTTP-svar med oprettet anmodning | højst 1.000 ms |
| K2: Første anmodning i en frisk kø | Afsendelse af den første oprettelses-POST → første matchende modtagelse hos en af sessionens tre undervisere (`HelpRequestCreated` via SignalR) | højst 10.000 ms |
| K3: Undervisersvar | Afsendelse af underviserens svar-POST → matchende `HelpRequestAnswered` modtaget i ejerens SignalR-callback | højst 10.000 ms |

Grænserne er inklusive: præcis 1.000/10.000 ms overskrider ikke kravet. Fejl og manglende levering inden fristen giver et fejlet krav. Uden observationer er K1/K3 `not-measured`. Annullerede prøver tælles som ufuldstændige (`incomplete`); K2 med færre end `Lessons` prøver er også `incomplete`. Det er ikke et bestået resultat. Andre HTTP-kald, forbindelsestider og genindlæsninger er diagnostik; de får ikke et selvstændigt krav om ét sekund.

K2 måles kontrolleret **én gang pr. frisk testlektion** med tom kø. En lokal lås holder andre oprettelser tilbage, indtil den første POST er afsluttet; derefter fortsætter de normalt samtidig. Den tidligste matchende SignalR-modtagelse blandt de tre validerede undervisere afslutter målingen. Kontrollér `N = Lessons` i hovedmålingen, som standard tre. Senere overgange fra tom til ikke-tom kø indgår ikke i denne kontrol. Et uklart udfald efter HTTP-afsendelse udløser ikke et nyt forsøg mærket som den første anmodning.

Den nuværende K3-probe dækker **almindelige digitale svar uden FAQ**, korreleret med præcist request-ID. Fysisk Start/Complete og kanoniske FAQ-broadcasts registreres som diagnostik og dokumenterer ikke K3 for disse varianter.

HTTP-afsendelse og SignalR-modtagelse bruger samme monotone `Stopwatch` i generatoren. Modtager, session, request-ID, ændring og hændelsestype skal matche. Beskeder før HTTP-svaret gemmes og bindes til svarets faktiske request-ID. K2/K3's ti sekunder regnes fra HTTP-afsendelsen, inklusive tid brugt på HTTP-svaret.

Playwright-browserklienter er **valgfri diagnostik** af synlig feedback. K2/K3 vurderes ved SignalR-modtagelse; browserrendering er ikke en del af disse acceptkriterier. Aktivér eksempelvis `"BrowserClients": 2, "FrontendUrl": "http://localhost:7000"`, eller brug `browser-smoke.cmd`. Browserne giver ekstra identiteter og forbindelser og skal angives i miljøbeskrivelsen. UI-fejl og screenshots bevares særskilt.

## Identitet og testdata

HTTP-klienterne deler en transportpulje med hoejst 1.024 samtidige forbindelser pr. gateway. Studenternes SignalR-forhandling/fallback deler en separat pulje med hoejst 2.048 forbindelser, saa langvarige fallback-kald ikke optager forretningskaldenes pladser. WebSockets beholder deres normale transport. Det undgaar en inaktiv HTTP-pulje pr. identitet; den tidligere graense paa 128 forretningsforbindelser kunne selv skabe ventetid ved mange genindlaesninger.

Hver identitet har stadig sin egen `HttpClient`, `CookieContainer` og eventuelle JWT; cookieheaderen saettes pr. kald, og `Set-Cookie` opdaterer kun den paagaeldende identitet. SignalR bruger samme individuelle cookiecontainer. Ventetid paa en ledig HTTP-forbindelse indgaar i HTTP- og K1/K2/K3-tiderne; ingen kald springes over. Puljegraenserne er egenskaber ved generatoren og skal med i miljoebeskrivelsen.

Ressourceopsamling laeser Windows-procesdata uden at holde laasen til HTTP-, SignalR- eller forbindelsesmaalinger. Langsomme OS-opslag maa ikke standse klienternes arbejde. Offline-selvtesten verificerer dette med et bevidst blokeret procesopslag.

Hver studerende får sin egen beskyttede `bifrost-anonymous-user`-cookie gennem `GET /api/students/me`. Én `CookieContainer` deles mellem den studerendes HTTP-klient og SignalR-forbindelse. Identitet og forbindelse genbruges gennem sessionen; login, tilslutning og første normale læsninger sker under ramp-up.

Underviserlogin tilføres gennem miljøvariabler:

- Tre separate JWT'er i `BIFROST_PERF_TEACHER_1_JWT`, `_2_JWT`, `_3_JWT`. Alle tre kontrolleres gennem `/api/teachers/me` og skal opfylde eksisterende issuer, audience, udløb, `Teacher`-rolle og `Bifrost`-scope.
- Alternativt tre par `BIFROST_PERF_TEACHER_1_USERNAME` / `BIFROST_PERF_TEACHER_1_PASSWORD` og tilsvarende for 2 og 3. Playwright følger gatewayens login → VarService → OIDC code/PKCE → gateway. Den reelt udstedte gatewaycookie bruges til HTTP og SignalR. Kun tre udfyldte JWT-variabler vælger JWT-tilstand.

Testen opretter lektioner gennem `/api/teachers/sessions`; øvrige undervisere tilsluttes med `TeacherCode`, og studerende bruger studenterkoden. Kurser har ingen separat offentlig oprettelsesrute. Warm-up bruger egne identiteter og lektioner, som lukkes og API-slettes før hovedmålingen.

Særskilt ejede testkonti kan oprettes med `--provision-teachers`, når de tredive username/password-par og `BIFROST_PERF_IDENTITY_CONNECTION_STRING` er sat til en isoleret Var-testdatabase. Det bruger VarServices normale ASP.NET Identity `UserManager`, hashing og rolleclaims. Eksisterende konti uden ejerskabsmarkør afvises. Databaseadgangen bruges kun administrativt; alle testflows og efterfølgende login går gennem gatewayen. De lokale Development-seedkonti skal suppleres med slots 4-30.

## Profil og konfiguration

| Fase | Standard | Belastning |
|---|---|---|
| Warm-up | 60 sekunder | 10 aktive brugere; særskilt kørsel, målinger udelades |
| Ramp-up | 300 sekunder | 0 → 1.285 studerende; autentifikation og SignalR |
| Steady-state | 600 sekunder | 1.156 lytter/genindlæser; 129 aktive; 30 undervisere |
| Ramp-down | 60 sekunder | Forbindelser lukkes gradvist; oprydning kontrolleres |

NBomber bruger en closed model med vedvarende aktører pr. scenariekopi. Konsollens `requests` og `latency` beskriver hele iterationer med flere handlinger, venten på notifikationer og pauser (standard 15 sekunder for studerende og 2 sekunder for undervisere). En studentiteration på cirka 15 sekunder er derfor ikke HTTP-svartiden. Vurder K1–K3 i `report.html`/`summary.json`, hvor de relevante operationer måles hver for sig. Smoke har to warm-up-brugere i 10 sekunder og derefter 10 studerende, tre aktive, tre undervisere og 10/30/5 sekunders ramp-up/steady/ramp-down.

Standardprofilen bruger 129 aktive studerende (afrundet op til 10 %); med det nye samlede antal er 1.156 lyttende. `ActiveStudents` kan tilpasses i egen JSON. Hver af de 10 sessions har tre separate undervisere, altså 30 i alt, og profilen giver 1.315 forbindelser uden valgfrie browserobservatører.

Tilpas med `--config <jsonfil>`; udeladte felter får standardværdier fra `Configuration/PerformanceOptions.cs`. Gatewaymiljøvariablen overstyrer JSON.

| Felt | Standard / betydning |
|---|---|
| `Students`, `ActiveStudents`, `WarmupStudents` | 1285, 129, 10 |
| `Lessons`, `LessonWeights` | 10, `[1,1,1,1,1,1,1,1,1,1]`; deterministisk vægtet fordeling |
| `WarmupSeconds`, `RampUpSeconds`, `SteadySeconds`, `RampDownSeconds` | 60, 300, 600, 60 |
| `StudentPauseSeconds`, `TeacherPauseSeconds`, `PauseJitterFraction` | 15, 2, 0.2; uniform ±20 % variation |
| `DigitalRequestFraction` | 1.0; kan sænkes for fysisk hjælp |
| `CancelRequestFraction`, `FollowUpFraction`, `FaqAcceptFraction` | 0.05, 0.1, 0.8; betingede sandsynligheder |
| `RequestTimeoutSeconds` | 30; transporttimeout, adskilt fra K1's krav på ét sekund |
| `NotificationTimeoutSeconds` | **10, fast** for K2/K3; beholdt af hensyn til JSON-kompatibilitet; andre værdier afvises |
| `ResourceProcessIds` | `[]`; faktiske backendprocesser ud over generatoren |
| `BrowserClients`, `FrontendUrl` | 0, ingen; højst fem ekstra browserobservatører |
| `EnvironmentDescription`, `OutputDirectory` | Beskriv miljøet; standardoutput er `bin/Release/net10.0/reports/<run-id>/` |

Studerende har højst én åben anmodning pr. lektion og følger faktisk status for reservation, svar, FAQ, annullering og opfølgning. Undervisere genoptager egne reservationer efter fejl; HTTP 204 ved en konkurrerende reservation er en normal tom kø. Korrelerede flows afventer deres besked, før den efterfølgende pause begynder; dette påvirker den faktiske aktivitetsfrekvens.

Notifikationer udløser klientens relevante genhentninger af kø, egne anmodninger, detaljer, FAQ, statistik og sessionsdata. Gentagne ugyldiggørelser af samme ressource samles under en igangværende genhentning som en tilnærmelse til Angular `httpResource`. Alle SignalR-beskeder tælles. Lyttere modellerer sessionssiden, aktive studerende køsiden, og hver underviser overvåger alle lektioner.

## Resultater og miljø

`Artifacts:` viser resultatmappen. `report.html`, `raw.json`, `summary.json` og CSV-filer indeholder kravstatus, rå operationer, belastning, faktisk antal forbindelser, afbrydelser, HTTP/s, p50/p95/p99, maksimum og fejl. Generelle kolonner med `≥ 1000 ms` er beskrivende; K1–K3 vurderes særskilt efter tabellen ovenfor. Notifikationstimeout er en fejl, ikke en målt leveringslatens. `--render-report <mappe>` gendanner HTML fra rådata; det kan ikke skabe manglende prøver fra gamle kørsler.

HTTP-tiden stopper, når hele svarbody er modtaget; DTO-deserialisering ligger uden for målingen. SignalR-negotiate og forbindelsesopstart måles særskilt. Forbindelseskurven omfatter studerende og tre undervisere: 1.288 ved fuld standardlast. Kontrollér også, at alle 1.285 unikke studerende er forbundet gennem steady-state. Planlagte annulleringer og lukninger registreres særskilt fra fejl.

Ressourcemålinger gælder kun generatoren og angivne PID'er. Brug serviceeksekverbarens PID; Aspires `executable.pid` kan være en `dotnet`-launcher med selve servicen som underproces. Registrér særskilt PostgreSQL-forbindelser samt outbox-/RabbitMQ-kølængder og ældste beskeds alder. Generatorens CPU/RAM beskriver ikke serverens forbrug.

Notér commit, konfiguration, OS/CPU/RAM, backendens buildtype, containergrænser, netværk og Generalization-mode. Brug et isoleret testdatasæt uden gammel beskedophobning. **Den nuværende backend er ikke dokumenteret bestået ved 1.285 studerende efter K1–K3.** De tidligere 100-brugerprøver og den fulde kørsel `20261105T093042273Z-320c64` brugte andre måleregler og afvises som verifikation af disse krav.

## Oprydning

Klienter lukkes før testlektioner API-slettes. Testen kontrollerer 404/fravær i undervisernes lister og nul resterende klientforbindelser. Ctrl+C forsøger samme oprydning og eksport. Et hårdt stop kan efterlade lektioner, hvis ID'er findes i `*-sessions.json`; slet kun disse gennem det normale autentificerede API.

API-sletning fjerner ikke alle tilknyttede anmodninger, beskeder, deltagelser og FAQ-/Mimir-data. Fuld nulstilling kræver en disponibel testdatabase og miljøets normale administration. Serverens forbindelsesregistry skal kontrolleres med servertelemetri.

Seedkontiene bevares. Kun konti oprettet med projektets ejerskabsmarkør kan slettes med `--delete-provisioned-teachers`, efter at deres lektioner er fjernet; `knud`, `bob` og de automatiske seedkonti berøres ikke.
