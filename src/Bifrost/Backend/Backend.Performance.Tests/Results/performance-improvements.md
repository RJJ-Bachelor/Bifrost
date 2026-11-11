# Performance: rettelser og aktuelle krav

Kravene er nu afgrænset til tre konkrete flows. **Ingen eksisterende 100-brugerprøve eller fuld kørsel dokumenterer, at den nuværende backend består K1–K3 ved 1.285 studerende.** Den aktuelle standardprofil er 1.285 studerende, heraf fortsat 100 aktive og dermed 1.185 lyttende, plus tre undervisere. Warm-up/ramp-up/steady-state/ramp-down er fortsat 60/300/600/60 sekunder. Gamle rapporter og rådatasnapshots bevares uændret som historik.

## Fuld kørsel med fejl: 1.000 studerende

Kørsel `20261105T140945381Z-3f7693` matcher terminaludskriften med 51.886 studentiterationer og 659 underviseriterationer. Den brugte K1–K3 og den daværende backend, før den seneste forenkling. Alle tre krav fejlede:

| Krav | Faktiske observationer |
|---|---|
| K1 | 440 oprettelser; 79 HTTP-timeout ved cirka 30 sekunder og yderligere 197 vellykkede oprettelser over ét sekund. |
| K2 | Tre første-kø-prøver; én bestod på 2,08 sekunder, to nåede ikke en underviser inden ti sekunder. |
| K3 | 117 digitale svar; alle ventetider endte uden matchende besked inden ti sekunder. |

Der var 63.995 HTTP-kald med 83 klienttimeouter og ingen fejlstatuskoder registreret. Alle 1.003 SignalR-forbindelser blev oprettet og lukket planlagt; ingen reconnects. Tre senere `HelpRequestAnswered` med samme request-ID som en besvarelse ankom efter cirka 182, 321 og 359 sekunder. Leveringsforsinkelsen er derfor reel. Der blev kun målt ressourcer for generatoren, så den præcise serverflaskehals er ikke dokumenteret i denne kørsel.

NBombers cirka 15-sekunders studentlatens indeholder den konfigurerede tænketid. Den er ikke HTTP-svartiden. De 82 + 120 fejlede iterationer omfatter de underliggende HTTP-/notifikationsfejl og skal ikke lægges til som nye, uafhængige hændelser. [Lokal rapport](../bin/Release/net10.0/reports/20261105T140945381Z-3f7693/report.html).

## Diagnose ved 1.285 studerende

Den fulde kørsel `20261105T151219011Z-9dc83d` havde 1.288 forbindelser uden forbindelsesfejl, men 131 af 400 vellykkede oprettelser tog over ét sekund (maksimum 4.856,57 ms). To af tre første-kø-notifikationer kom først efter cirka 43 og 64 sekunder. Alle 137 svarprøver fejlede; tre matchende svar kunne efterfølgende findes efter cirka 293, 401 og 516 sekunder. Kravgrænserne og korrelationen forklarer ikke forsinkelsen væk.

Der er rettet to konkrete former for forstærkning:

- En generel køændring var én database-/outboxbesked pr. deltager. Den er nu én `session.changed`-besked med den autoriserede modtagerliste. Gna bruger SignalRs normale brugerlevering til disse realtime-opdateringer uden at oprette en Notification og endnu en outboxrække for hver deltager. Klientformat, genindlæsninger og private personlige svar bevares. Modtagerlisten og private requestfelter sendes ikke til klienterne.
- Under den første korte højlastprøve blev 37 af 40 Eir-forbindelser observeret ventende på advisory-låse, mens tre transaktioner med låsen ventede på forbindelser til andre modulers forespørgsler. Skrivetransaktioner får nu en af otte pladser før databaseforbindelsen/transaktionen åbnes. `TakeNext` bruger samme grænse. De eksisterende databasekølåse og generationskontroller bevares; der er stadig plads til læseforespørgsler i standardpuljen på 40.

Den første diagnostiske prøve `20261105T184140134Z-f94f45` brugte den nye fælles køopdatering, men endnu ikke skrivegrænsen. Den fejlede og bevares: K1 havde 136 prøver, 116 fejl og maksimum cirka 31 sekunder; K2 var ufuldstændig, K3 blev ikke målt. Den afslørede forbindelses-/låseproblemet ovenfor. Prøven havde kortere 60/90/15-sekunders målefaser og kan ikke erstatte den fulde profil. En tidligere opstartsfejl `20261105T183935441Z-df5502` og efterfølgende bestået student-preflight `20261105T184120583Z-3ef019` bevares også. Intet af dette dokumenterer endnu fuld kapacitet.

## Efterfølgende højlastprøver og lokale socketfejl

Den korte diagnose `20261105T190037380Z-32c3e3` havde både fælles køopdatering og otte skrivepladser, men brugte 60/90/15 sekunder i målefaserne. Den nåede kun 710 samtidige forbindelser og fik 512 forbindelsesfejl, herunder 450 HTTP 504 ved SignalR-negotiering. K1: N=141, 113 fejl, alle 141 over ét sekund, maksimum 33.962,66 ms og 36 annullerede prøver. K2 bestod sine tre prøver med maksimum 6.374,92 ms. K3 blev ikke målt, fordi alle fem `take-next`-kald fik timeout. Den korte ramp-up havde fem gange den krævede ankomstrate og kan ikke godkende den fulde profil.

Den efterfølgende kørsel `20261105T191241132Z-fa7881` brugte de krævede 300/600/60-sekunders faser og separat warm-up. Den stoppede efter 659,35 målte sekunder, før steady-state og populationskontrollen var afsluttet. Højeste faktiske antal forbindelser var 1.278; der var 1.090 reconnect-hændelser og 782 genoprettelser. 41.187 HTTP-kald indeholdt 10.674 fejl. K1: 50 vellykkede oprettelser, heraf 37 over ét sekund og maksimum 29.664,10 ms. K2: tre prøver, to fejl, maksimum 22.076,83 ms. K3: tre prøver, alle fejlede, maksimum 21.273,67 ms. Rapportens `failed` og de ufuldstændige belastningsfaser skal bevares ved sammenligning; den fulde profil bestod ikke.

De første cirka 270 sekunder af den fulde ramp-up havde ingen HTTP-fejl og typisk 125–192 ms median. Ved overgangen til aktivitet voksede svartiderne og forbindelsesfejlene kraftigt. Generatoren registrerede 6.460 studentkald til SignalR-negotiering, heraf 1.482 fejl, som del af reconnect-belastningen. Ressourceprøverne omfatter her de faktiske API-processer, ikke Aspire-processernes `dotnet run`-wrappere.

Gatewayens strukturerede advarsler dokumenterer **63 socketfejl 10055**: 43 mod den lokale Eir-destination `localhost:6000` og 20 mod Gna på `localhost:6001`. Systemet manglede socketbufferplads, eller en kø var fuld. Desuden fandtes 26 fejl 10054 og to fejl 10053 i de bevarede advarsler. Dette er logobservationer, ikke nye uafhængige HTTP-fejl. [Sanitiseret evidens](../bin/Release/net10.0/reports/20261105T191241132Z-fa7881/socket-evidence.json) indeholder kodeoptællinger, API-stier uden forespørgselsparametre og korte undtagelseseksempler. Historiske `raw.json`, rapporter og målinger er uændrede.

Windows har lokalt den dynamiske TCP-række 49152–65535, i alt 16.384 porte. Hver student havde sin egen HTTP-transportpulje: den første genindlæsning starter seks GET-kald parallelt, og almindelige notifikationsgenindlæsninger starter op til tre. SignalR har desuden sin egen transport. Aspire-proxyerne tilføjer lokale TCP-hop gennem gateway og backend. Som kapacitetsoverslag kan tre parallelle kald fra 1.285 klienter over fire lokale hop svare til 15.420 TCP-hopforbindelser, plus cirka 5.140 til SignalR. **Overslaget er ikke en måling af samtidige forbindelser eller unikke porte.** Genbrug, idle-udløb, protokol og portgenbrug ændrer det faktiske forbrug. Fejl 10055 dokumenterer lokal netværksressourcemangel, men afgør ikke alene, om årsagen er dynamiske porte eller bufferpres. Forbindelsesbelægningen blev ikke målt under denne kørsel.

En ændring af generatorens transport skal bevare 1.285 særskilte identiteter og cookiecontainere til både HTTP og SignalR. Fælles netværksforbindelser må ikke blive til fælles cookies. Kravgrænser, modtagerkontrol og de normale notifikationsgenindlæsninger må heller ikke svækkes for at få rapporten til at bestå.

## Tidligere smoke med 10 studerende

Kørsel `20261105T134738793Z-43538d` brugte de aktuelle K1–K3: **alle tre bestod** med nul fejl, overskridelser og annullerede kravprøver. K1 havde 35 prøver og maksimum 482,5974 ms; K2 én prøve og 665,4236 ms; K3 37 prøver og 1.888,5969 ms. K2 dækkede den ene friske lektion som forventet.

Profilen var 10 studerende, tre aktive, én lektion og tre undervisere; separat warm-up med to brugere i 10 sekunder, derefter 10/30/5 sekunders ramp-up/steady-state/ramp-down. 2.313 HTTP-kald gav nul HTTP-fejl. Der var højst 13 forbindelser, 13 planlagte lukninger og nul tilbage; to øvrige GET-kald blev klientannulleret og er bevaret særskilt.

[Rapport](criteria-smoke-20261105T134738Z/report.html), [rådata](criteria-smoke-20261105T134738Z/raw.json) og [præcist miljø](criteria-smoke-20261105T134738Z/README.md) er kopieret til et nyt snapshot. Lokal Aspire/Development brugte Debug-backend, Release-generator, Docker/PostgreSQL/RabbitMQ og `Generalization=Fake`. Kun generatorens ressourcer blev målt; browserdiagnostik var slået fra. Prøven dokumenterer de observerede krav ved 10 studerende, **ikke kapacitet ved 1.285**.

## K1–K3

| Krav | Start → slut | Grænse |
|---|---|---|
| K1 | Studentens oprettelses-POST → færdigt HTTP-svar med registreret anmodning | højst 1 sekund |
| K2 | Første oprettelses-POST i en frisk kø → første matchende modtagelse hos en af de tre undervisere (`HelpRequestCreated` via SignalR) | højst 10 sekunder |
| K3 | Underviserens svar-POST → matchende `HelpRequestAnswered` hos anmodningens ejer via SignalR | højst 10 sekunder |

Tiderne regnes fra faktisk HTTP-afsendelse med generatorens monotone ur. K2/K3 inkluderer HTTP-svartiden. Det er modtagelsen i SignalR-callbacken, der afslutter målingen. Browserrendering er valgfri diagnostik. Øvrige HTTP-kald, genindlæsninger og forbindelsestider er diagnostik med egne percentiler; de har ikke K1's étsekundskrav.

K2 har én kontrolleret prøve pr. frisk testlektion, forventet `N = Lessons` i hovedmålingen (standard tre). Første POST holdes adskilt fra konkurrerende oprettelser frem til HTTP-svaret; efterfølgende oprettelser kører normalt samtidig. Den tidligste matchende modtagelse blandt de tre validerede undervisere afslutter målingen. Senere overgange fra tom til ikke-tom kø er ikke dækket. Annullerede prøver og K2 med færre end `Lessons` prøver er ufuldstændige (`incomplete`), ikke bestået.

K3's nuværende probe dækker almindelige digitale svar uden FAQ med præcist request-ID. Fysisk Start/Complete og kanoniske FAQ-broadcasts er diagnostik og dokumenterer ikke K3 for disse varianter.

## Enkle backendrettelser

- [RabbitMQ-bussen](../../Shared/Shared.Infrastructure/Persistence/Messaging/RabbitMqMessageBus.cs) genbruger én publisherkanal og deklarerede exchanges. Beskeder publiceres **én ad gangen**, og hvert kald afventer brokerens bekræftelse. Persistent levering, `mandatory`, stabile besked-ID'er og synlige fejl bevares. Fejl udløser en ny kanal ved næste forsøg.
- [Eirs outbox](../../Shared/Shared.Infrastructure/Persistence/Outbox/OutboxDispatcher.cs) og [Gnas leveringsoutbox](../../GnaService/GnaService.Infrastructure/Persistence/Outbox/NotificationOutboxDispatcher.cs) sender og gemmer én besked ad gangen. Der hentes højst 50 ventende rækker ad gangen; næste side behandles straks ved fuld succes. Tom kø og fejl udløser ventetid. Et nedbrud mellem afsendelse og databasegemning kan medføre genlevering med samme ID.
- [Databaseforbindelser deles pr. navngivet database](../../Shared/Shared.Infrastructure/Persistence/DatabaseDataSourceRegistration.cs). Eirs moduler bruger én fælles pool med standardloft **40 forbindelser**. En eksplicit konfigureret grænse bevares. PostgreSQLs samlede loft deles stadig med øvrige services.
- Køstartkontrollen bruger en SQL-eksistenskontrol frem for at hente hele køen med beskeder. Gnas notifikationsconsumer kan behandle otte beskeder samtidig med prefetch 32; øvrige consumers beholder som standard én behandling ad gangen. ACK/NACK sendes enkeltvis.

Det ekstra batchinterface, overlappende publisher confirms, fælles databasecheckpoint, 20-siders sweep og særskilte shutdown-token er fjernet. Grænsen på 50 er alene en afgrænset databaselæsning. Den enkle model har flere databasekald; forenklingen dokumenterer ikke i sig selv bedre kapacitet. En ny belastningsprøve skal afgøre, om K1–K3 kan opfyldes.

## Hvad de tidligere kørsler kan bruges til

| Historisk kørsel | Dokumentation |
|---|---|
| `20261104T202645742Z-080f6a`, grundlag for `performance.pdf` | 1.003 forbindelser og 51.952 HTTP-kald uden HTTP-fejl, men 596 korrelerede notifikationstimeouts. Efterfølgende blev 167.432 ventende Eir-beskeder observeret. |
| `20261105T092542092Z-8100f5`, 100 studerende | 10.968 HTTP-kald uden HTTP-fejl og 99 modtagne korrelerede beskeder under de daværende måleregler. [Historisk rapport](delivery-fix-100-20261105T092542Z/report.html). |
| `20261105T093042273Z-320c64`, 1.000 studerende | Fuld profil med den tidligere implementering og målemetode. Der blev fortsat observeret voksende køophobning. [Oprindelig lokal rapport](../bin/Release/net10.0/reports/20261105T093042273Z-320c64/report.html). |

**100-brugerprøven og den fulde kørsel afvises som verifikation af de nye K1–K3.** De brugte andre operationer, modtagerkriterier og leveringsfrister og blev kørt før den seneste forenkling. En gammel rapports `completed` eller `0 errors` gælder dens egen målemetode. Genrendering kan ikke tilføje manglende K2-prøver eller gøre historiske data til en ny test.

Diagnostikken fandt både beskedophobning og tidligere PostgreSQL `53300` (for mange forbindelser). Senere enkelte åbningstimeouts havde en anden fejltype; direkte Npgsql-prober reproducerede dem ikke. Det er ikke grundlag for at erklære en bestemt DNS-, IPv6- eller proxyfejl løst.

## Ny verifikation

Release-build af integrationstest- og performanceprojektet er gennemført uden advarsler eller fejl. De 19 relevante outbox-/databasepuljetests, performanceprogrammets `--self-test` og full-scriptets dry-run består. Dry-run viser 1.285 studerende, 100 aktive og tre undervisere. Dette er kode- og opsætningskontrol; fuld kapacitet ved 1.285 studerende er endnu ikke verificeret.

Efterfølgende lokal verifikation: `20261105T150621446Z-3682af` validerede tre separate underviseres rigtige OIDC-login gennem gatewayen uden passwordprompt. Scriptsene bruger nu Development-seedets `Pass123$` for de tre standardkonti ved en lokal gateway; eksplicitte miljøvariabler har forrang. VarServices oprindelige HTTP 500 skyldtes timeout ved åbning af PostgreSQL-forbindelsen. Efter genstart af afhængighederne manglede databasen `var`; schema-adoptionen er rettet til kun at køre mod en eksisterende, tilgængelig database, hvorefter de normale migrationer kan oprette en manglende database.

Den efterfølgende [smoke `20261105T150704191Z-e3e9a2`](../bin/Release/net10.0/reports/20261105T150704191Z-e3e9a2/report.html) bestod med 10 studerende, tre aktive og tre undervisere: 1.825 HTTP-kald, nul HTTP-fejl og alle K1–K3 bestået. K1: N=17, maksimum 908,23 ms; K2: N=1, maksimum 1.560,42 ms; K3: N=15, maksimum 5.813,10 ms. Ingen kravprøver blev annulleret eller overskred deres grænse. Eir/Gna/Var var genopbygget via Aspire; PostgreSQL og RabbitMQ var raske. Det verificerer den forenklede kode ved den lille profil, ikke kapacitet ved 1.285 studerende.

Start et isoleret Aspire-miljø uden gammel outboxophobning, og kør fra repository-roden:

```powershell
.\scripts\Test\Perfomance\smoke.cmd -OpenReport
.\scripts\Test\Perfomance\full.cmd -OpenReport
```

Kontrollér K1–K3 særskilt, herunder K2's antal prøver, faktiske 1.285 studentforbindelser gennem steady-state samt HTTP-fejl og leveringsfejl. Standardlast med tre undervisere giver 1.288 forbindelser uden valgfrie browserobservatører. Gem rådata, konfiguration, outbox-/RabbitMQ-kølængder og ressourcer fra de faktiske backendprocesser. Ingen målinger, fejlede kørsler eller timeouts fjernes fra en sammenligning. En lille prøve kan verificere måleopsætningen, men kan ikke godkende fuld kapacitet.
