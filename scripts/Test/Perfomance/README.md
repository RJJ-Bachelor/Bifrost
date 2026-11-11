# Performance-scripts

Kør scriptsene i Windows, eller dobbeltklik på `.cmd`-filerne. Stier til projektet findes automatisk, uanset hvilken arbejdsmappe du starter fra. Mappen hedder `Perfomance` som angivet i opgaven.

1. Kør `setup.cmd` første gang. Det bygger Release, installerer Playwright Chromium og kører projektets offline selvtest. Kræver .NET 10 SDK og netadgang ved første installation; Aspire behøver ikke køre.
2. Start Docker Desktop, vent på at motoren kører, og kør `start-aspire.cmd`. Det starter Yggdrasil/Aspire i baggrunden med projektets normale Development-profil og venter på de nødvendige services.
3. Kør `smoke.cmd`. Gennemgå den lille prøvekørsels rapport.
4. Kør `full.cmd` for normalprofilen. Kør `full-100.cmd` separat for worst-case-profilen.

| Script | Funktion |
| --- | --- |
| `setup.cmd` | Release-build, Chromium og offline selvtest |
| `start-aspire.cmd` | Start Yggdrasil, hvis det ikke kører, og vent på database, broker, backendservices og gateway |
| `check.cmd` | Lille gatewaykontrol af studentidentitet og cookie; ingen underviserlogin |
| `check-teachers.cmd` | Studentkontrol og login/rolle for alle 30 undervisere; ingen belastning eller oprettelse af lektioner |
| `smoke.cmd` | 10 studerende, tre aktive og 30 undervisere |
| `browser-smoke.cmd` | Smoke med to ekstra Playwright-klienter og 60 sekunders steady-state; kræver frontend på `http://localhost:7000` |
| `full.cmd` | Standardprofil med 1.285 studerende: 129 aktive, 1.156 lyttende, 10 sessions og 30 undervisere |
| `full-100.cmd` | 100 %-profil med 1.285 aktive studerende, én HelpRequest pr. studerende (1.285 i alt) og 30 undervisere |
| `RunPerfomanceTest.cmd` | Kører setup, Aspire-start, gatewaykontrol, smoke og til sidst `full.cmd -OpenReport` |
| `stop-aspire.cmd` | Stopper AppHost og Aspire-oprettede backendservices, PostgreSQL og RabbitMQ uden at slette volumes |

Alle scripts bygger som udgangspunkt projektet. Belastningsprofilerne sørger også for Chromium, når login eller UI kræver det. Brug `-NoBuild -SkipBrowserInstall`, når opsætningen er klar, og koden ikke er ændret. Aspire startes separat.

Når Docker Desktop kører, kan hele rækkefølgen køres med `RunPerfomanceTest.cmd`. Scriptet stopper ved fejl i setup, Aspire, check eller smoke. Det kører derfor ikke den lange full-test, hvis miljøet ikke først er kontrolleret.

## Efter genstart af Windows

Aspire og gatewayen starter ikke automatisk efter en genstart. Start Docker Desktop og vent på, at Docker-motoren er klar. Kør derefter:

```powershell
.\scripts\Test\Perfomance\start-aspire.cmd
.\scripts\Test\Perfomance\check.cmd
.\scripts\Test\Perfomance\smoke.cmd -OpenReport
.\scripts\Test\Perfomance\full.cmd -OpenReport
```

Fortsæt til næste kommando, når den foregående er lykkedes. `start-aspire.cmd` finder projektet uanset den aktuelle arbejdsmappe, bruger Aspire CLI og venter på `postgres`, `rabbitmq`, `varservice`, `eirservice-api`, `gnaservice-api`, `mimirservice-api` og `heimdallgateway`. Til sidst kontrollerer den også gatewayens student-endpoint, så en service der er stoppet under en database-migration ikke fejlagtigt erklæres klar. Ventetiden er højst 180 sekunder pr. ressource og for gatewaykontrollen; brug fx `start-aspire.cmd -TimeoutSeconds 300`, hvis den første opstart tager længere tid. Det genstarter ikke en AppHost, som allerede kører, og nulstiller ikke databaser. Det foretager ingen loginforsøg. Du kan fortsat starte Yggdrasil fra IDE'et i stedet.

De øvrige performance-scripts starter ikke miljøet. Fejlen `connection refused (localhost:6004)` før målingen betyder, at gatewayen ikke kan kontaktes på den adresse. Kør opstartshjælperen, og læs dens eventuelle fejl, før du prøver testen igen. `check.cmd` verificerer studentcookieflowet; `smoke.cmd` kontrollerer også underviserlogin og et lille hjælpeflow. Start ikke `full.cmd`, hvis disse fejler.

`start-aspire.cmd -DryRun` viser de planlagte kommandoer uden at starte services eller kontakte Docker/Aspire. Hjælperen kræver Docker Desktop, .NET 10 SDK og Aspire CLI i PATH.

Standardprofilen bruger 129 aktive studerende (afrundet op til 10 %); de resterende 1.156 lytter og genindlæser kun efter relevante SignalR-events. De 1.285 studerende fordeles ligeligt på 10 sessions, og hver session har tre separate undervisere, altså 30 undervisere i alt. `ActiveStudents`, `Lessons` og `LessonWeights` kan tilpasses i egen JSON. Aktive studerende opretter højst én help request hver (`MaxRequestsPerActiveStudent`). Det giver 1.315 forbindelser i standardprofilen. Warm-up og fasernes varigheder er fortsat 60/300/600/60 sekunder; smokeprofilerne er uændrede.

`full-100.cmd` er en separat worst-case-kørsel for samtidige HelpRequests. Alle 1.285 studerende er aktive og opretter præcis én HelpRequest, altså 1.285 requests i alt. Den bruger samme 10 sessions, tre separate undervisere pr. session og får sin egen rapportmappe; resultaterne skal ikke blandes med normalprofilens målinger. Flere requests pr. studerende hører til en separat stress-/kapacitetstest og er ikke nødvendige for P1, P4 eller P5.

Testen laver ikke kø-GET, statistik-GET, polling eller student-reloads. Den sender HelpRequest-POST'en, læreren bruger `take-next` efter `HelpRequestCreated` og sender et svar, og student-/lærerforbindelserne holdes åbne for at måle SignalR-modtagelser.

`RunPerfomanceTest.cmd` stopper automatisk AppHost, PostgreSQL, RabbitMQ og Aspire-services efter både succes og fejl. Persistent volumes bevares, så testdata og databasefiler slettes ikke. Hvis Aspire har stale DCP-state for containere, der allerede er væk, verificerer stop-scriptet dette og behandler oprydningen som afsluttet. Kør `stop-aspire.cmd` alene for manuel oprydning.

Gna's RabbitMQ-forbruger til notification-oprettelser kører med 8 parallelle consumers og prefetch 32 i performance-AppHosten. Det reducerer køen mellem Eir's notification-event og SignalR-dispatch uden at ændre notificationernes forretningsrækkefølge.

Gna's performance-AppHost bruger en eksplicit PostgreSQL-pool på 40 forbindelser. Det holder notification-dispatcheren inden for en forudsigelig forbindelsesgrænse under full-testen; det øger ikke testens tidsgrænser eller skjuler langsomme svar.

NBombers konsoltabel viser hele iterationer: `requests` tæller iterationer, og `latency` inkluderer flere HTTP-kald, venten og pauser (standard 15 sekunder for studerende og 2 sekunder for undervisere). Brug K1–K3 i `report.html`/`summary.json` til at vurdere kravene; konsollens cirka 15 sekunder for studerende er ikke oprettelses-POSTens HTTP-svartid.

## Krav i rapporten

| Krav | Måling | Grænse |
| --- | --- | --- |
| K1 | Studentens oprettelses-POST → færdigt HTTP-svar med registreret hjælpeanmodning | højst 1 sekund |
| K2 | Første oprettelses-POST i en frisk kø → første matchende modtagelse hos en af sessionens tre undervisere (`HelpRequestCreated` via SignalR) | højst 10 sekunder |
| K3 | Underviserens svar-POST → studentens matchende `HelpRequestAnswered`-notifikation | højst 10 sekunder |

K2 måles én gang pr. frisk lektion med den første POST adskilt fra konkurrerende oprettelser indtil HTTP-svaret. Den tidligste matchende modtagelse blandt de tre validerede undervisere afslutter målingen. Kontrollér `N = Lessons` (standard 10) i hovedmålingen. Senere overgange fra tom til ikke-tom kø er ikke dækket. Annullerede prøver og K2 med for få prøver er `incomplete`, ikke bestået. K2/K3's frist regnes fra HTTP-afsendelse; `NotificationTimeoutSeconds` er fast 10, og andre værdier i egen JSON afvises.

K3 måler kun SignalR-notifikationen; den måler ikke, om klienten efterfølgende henter eller viser køen. `take-next` er kun med, fordi en lærer skal have et konkret request-ID for at kunne sende svaret. Efter measured-fasen holdes forbindelserne åbne i et drain-vindue, og testen kræver, at alle oprettede requests har en matchende student-notifikation. Manglende requests registreres som K3-fejl.

Andre HTTP-kald, genindlæsninger, forbindelsestider og browserrendering er diagnostik. Generelle tællere med `≥ 1000 ms` gør ikke disse operationer til K1-fejl. `browser-smoke.cmd` er en valgfri undersøgelse af brugerfladen; K2/K3 afsluttes ved SignalR-modtagelse.

Tidligere 100-brugerprøver og den fulde kørsel `20261105T093042273Z-320c64` afvises som verifikation af de nye K1–K3. De historiske rapporter bevares. Den nuværende backend er endnu ikke dokumenteret bestået med 1.285 studerende efter disse krav.

## Login

Scriptsene bruger eksisterende `BIFROST_PERF_TEACHER_1_USERNAME` / `_PASSWORD` til `BIFROST_PERF_TEACHER_30_USERNAME` / `_PASSWORD`. Manglende brugernavne bliver `performance-teacher-1` til `performance-teacher-30`, som VarService seeder i Development. På en lokal gateway (`localhost` eller en loopback-IP) bruger alle 30 konti automatisk det hardcodede Development-password `Pass123$`, når password ikke er angivet i miljøet. Der kommer derfor ingen passwordprompt ved normal lokal kørsel. Brugerdefinerede brugernavne og eksterne gateways med manglende passwords giver fortsat en prompt.

Tre gyldige JWT'er i `BIFROST_PERF_TEACHER_1_JWT`, `_2_JWT` og `_3_JWT` bruges i stedet, hvis alle tre er angivet. En egen JSON-konfigurations `TeacherTokenEnvironmentVariables` respekteres også. Scriptsene logger ikke de anvendte passwords eller tokens og skriver dem ikke i rapporterne. Midlertidigt tilføjede miljøvariabler gendannes efter kørsel. Login går gennem gatewayens normale flows.

Miljøvariabler overstyrer altid det lokale standardpassword. Hvis du har valgt et andet seed-password, skal alle tre `_PASSWORD`-variabler sættes til det. Eksisterende konti får ikke nulstillet deres password ved Var-opstart. Var kan låse en konto midlertidigt efter mislykkede login; der foretages ingen automatiske loginforsøg igen. Brug `check-teachers.cmd` til hurtigt at kontrollere login før en ny fuld test.

## Eksempler

Fra repository-roden:

```powershell
.\scripts\Test\Perfomance\setup.cmd
.\scripts\Test\Perfomance\start-aspire.cmd
.\scripts\Test\Perfomance\check-teachers.cmd
.\scripts\Test\Perfomance\smoke.cmd -OpenReport
.\scripts\Test\Perfomance\browser-smoke.cmd -NoBuild -SkipBrowserInstall -OpenReport
.\scripts\Test\Perfomance\full.cmd -NoBuild -SkipBrowserInstall -OpenReport
```

Gatewayen er som standard `http://localhost:6004`. Brug `-GatewayUrl 'http://localhost:6004'` eller miljøvariablen `BIFROST_PERF_GATEWAY_URL` til at overstyre den. Ellers respekteres en eventuel `GatewayUrl` i din egen JSON.

```powershell
.\scripts\Test\Perfomance\full.cmd -ConfigPath .\min-profil.json
# Vis kommandoer og stier uden build, installation, login eller belastning:
.\scripts\Test\Perfomance\full.cmd -DryRun
# Offline selvtest via det fælles PowerShell-script:
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test\Perfomance\performance.ps1 -Mode SelfTest -NoBuild
```

`-ConfigPath` bruges kun med `full.cmd` / `-Mode Full` og erstatter hele standardprofilen med din JSON, inklusive eventuelle små brugerantal. Relative konfigurationsstier løses fra din aktuelle arbejdsmappe. Andre relative stier i JSON, fx `OutputDirectory`, løses fra repository-roden. Udeladte JSON-felter har projektets standardværdier.

`.cmd`-filerne venter på en tast, så output kan læses ved dobbeltklik. Sæt `$env:BIFROST_PERF_NO_PAUSE = '1'` i terminalen for at undgå dette. De returnerer programmets exitkode, også når en gennemført test har observerede fejl.

## Resultater og oprydning

Programmets `Artifacts:`-linje viser den præcise resultatmappe. Standard er `src/Bifrost/Backend/Backend.Performance.Tests/bin/Release/net10.0/reports/<kørsel>/` med `report.html`, rå JSON/CSV og NBomber-rapporter. `-OpenReport` åbner en rapport fra den aktuelle kørsel, også når testen har registreret fejl.

Fejl ved underviserlogin stopper før warm-up og måling. Rapporten viser da opstartsfejlen og oplyser, at der ikke er målt belastning; tomme måledata er ikke et resultat af en fuld test. Hver kørsel bruger en ny browserkontekst og en ny resultatmappe, så tidligere rapporter og loginsessioner genbruges ikke.

Hvis Var-login giver HTTP 500, så kontrollér især `postgres` og databasen `var` i Aspire samt VarServices logs. Gateway og Var kan stadig være markeret som kørende, selv om PostgreSQL er stoppet. Sørg også for, at `rabbitmq` er klar før belastningstesten. Den lille kontrol af studentcookie alene verificerer ikke database eller broker.

Efter genskabelse af en disponibel database kan logs vise `3D000` (manglende database) eller `42P01` (manglende tabel), selv om PostgreSQL igen er rask. Genopbyg og start/genstart da VarService og de berørte backendservices i Aspire, så deres normale migrationer og seed køres. VarServices opstart lader nu migrationen oprette en manglende database, før brugerdata seedes; tilpasningen af gamle Identity-tabeller køres kun mod en eksisterende database. Testscriptet ændrer ikke databasen direkte.

Ctrl+C forsøger normal oprydning og eksport. Sessioner slettes gennem API'et, men den eksisterende backend efterlader visse relaterede data. Se [projektets README](../../../src/Bifrost/Backend/Backend.Performance.Tests/README.md) for konfiguration, målingernes begrænsninger og fuld beskrivelse af testdata/oprydning. Scriptsene nulstiller ikke databaser eller sletter eksisterende rapporter.
