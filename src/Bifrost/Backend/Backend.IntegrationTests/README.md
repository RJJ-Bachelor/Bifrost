# Backend-integrationstest

Projektet indeholder afgrænsede integrationstest af API, messaging og persistens samt scenarierne fra rapporten. Afhængigt af scenariet indgår HTTP gennem Heimdall samt rigtige PostgreSQL-, RabbitMQ- og SignalR-forbindelser. `integration-tests.tex` er et rapportudsnit, som beskriver denne afgrænsning og de forventede resultater; det er ikke en registrering af en bestemt testkørsel.

## Testopsætning og afgrænsning

`Infrastructure/ReportScenarioFixture.cs` starter Heimdall og en samlet backend som to ASP.NET Core-hosts i samme testproces. Backendhosten registrerer Eir, Gna og Mimir med deres applikations- og infrastrukturlag. Backendkomponenterne deler host, fordi FastEndpoints har procesglobale registreringer. Kald gennem gatewayen, AMQP-beskeder og SignalR-levering bruger rigtige netværksforbindelser.

Testene i `Scenarios/` får egne PostgreSQL-databaser og en unik RabbitMQ-vhost. IT-05 i `PostgresRabbitMqPipelineTests` bruger egne databaser og en unik testkø. Migreringer og de relevante produktionskomponenter indgår i forløbene; `ReportScenarioFixture` starter også deres hosted services. Tests venter på observerbare tilstande med en deadline, herunder aktive consumers, gemte data og modtagne hændelser. Testdata genbruges ikke mellem scenarier, og fixture-ressourcer ryddes op efter testen. Hosttests deler collectionen `Backend hosts`, der kører uden parallelisering på grund af de procesglobale registreringer.

Følgende erstattes kontrolleret i scenarierne:

- Læreridentitet leveres som lokalt signerede JWT'er med kontrollerede claims. Den rigtige JWT-validering og autorisationspolitik bruges, men ekstern identitetsudbyder, loginflow og tokenudstedelse testes ikke.
- Gateway og backend deler en midlertidig databeskyttelsesprovider til elevcookies. Produktionsmiljøets lagring og distribution af nøgler indgår ikke.
- Generalisering bruger den rigtige beslutnings- og beskedbehandling med en `ILlmClient`-stub, der returnerer et fast svar. Testen vurderer ikke Geminis semantiske kvalitet eller den eksterne Gemini-forbindelse.
- Brokerudfald fremkaldes ved at lukke og blokere testens TCP-forbindelser til RabbitMQ gennem en lokal proxy. RabbitMQ-processen stoppes ikke, og andre vhosts påvirkes ikke.

Scenarierne tester komponenternes samspil. De dokumenterer ikke browseradfærd, selvstændige serviceprocesser, containernetværk eller en produktionsdeployment.

## Sammenhæng med rapporten

| Scenarie | Testfil og metode | Kontrol |
| --- | --- | --- |
| IT-01 Hjælpeforløb | `Scenarios/ReportScenarioTests.cs`: `IT01_HelpRequestThroughGatewayReachesQueueAndAnswerArrivesThroughGnaSignalR` | Oprettelse, kø, reservation og svar gennem Heimdall/Eir; privat SignalR-hændelse fra Gna med samme anmodning, session, generation og afsluttede status. |
| IT-02 Generalisering | `Scenarios/GeneralizationScenarioTests.cs`: `RelatedRequestsTravelThroughMimirAndRabbitMqAndBecomeOneFaqInEir` | Relaterede anmodninger behandles via RabbitMQ og Mimir; Eir gemmer én FAQ med forventet session, generation og kildeanmodninger. |
| IT-02 Afgrænsning | `Scenarios/GeneralizationScenarioTests.cs`: `IdenticalQuestionsInDifferentSessionsOrGenerationsRemainSeparate` | Identiske spørgsmål fra forskellige sessioner eller generationer generaliseres ikke sammen. |
| IT-03 Adgangskontrol | `Scenarios/ReportScenarioTests.cs`: `IT03_GatewayAndRealEirRejectInvalidCredentialsRolesAndSessionAccessWithoutLeakingData` | Tilladt adgang samt ugyldigt/udløbet token, forkert rolle/scope og manglende medlemskab; 401/403/404 og ingen private felter i afviste svar. |
| IT-04 Brokerudfald | `Scenarios/ReportScenarioTests.cs`: `IT04_RequestAndOutboxSurviveBrokerOutageAndReachMimirAfterReconnect` | HTTP-oprettelse under forbindelsesudfald gemmer anmodning og outbox med registreret leveringsfejl; samme besked publiceres og modtages i Mimir efter genopkobling. |
| IT-05 Genlevering | `Messaging/PostgresRabbitMqPipelineTests.cs`: `CommittedOutboxNotificationTravelsThroughRabbitMqAndIsStoredOnceByGna` | Samme besked-ID og indhold publiceres igen via rigtig RabbitMQ; Gna har fortsat én notifikation og én leveringspost i outbox. |
| IT-06 Genstart | `Scenarios/ReportScenarioTests.cs`: `IT06_PendingOutboxIsDeliveredAfterBackendRestartWithoutResubmittingTheRequest` | Backendhosten genstartes med ubehandlet outboxarbejde; den eksisterende anmodning og besked leveres efter genopkobling uden et nyt HTTP-oprettelseskald. |

`Messaging/UnifiedMessageDeliveryTests.cs` supplerer IT-05 med metadata, rollback, samtidighed og fælles idempotent receiver for lokal transport og RabbitMQ-adapteren. Dens RabbitMQ-variant bruger en kontrolleret testbus; den rigtige AMQP-genlevering verificeres i `PostgresRabbitMqPipelineTests`.

## Lokal kørsel

Brug .NET 10 samt dedikerede PostgreSQL- og RabbitMQ-testservere. PostgreSQL-brugeren skal kunne oprette og slette testdatabaser. RabbitMQ skal have management-plugin, og brugeren skal kunne oprette/slette vhosts samt tildele egne rettigheder. Management-API'et bruger brugernavn og adgangskode fra AMQP-adressen.

Sæt disse miljøvariable med dine egne testværdier, eksempelvis i PowerShell:

```powershell
$env:BIFROST_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=<database>;Username=<user>;Password=<password>'
$env:BIFROST_TEST_RABBITMQ = 'amqp://<user>:<password>@localhost:5672/'
$env:BIFROST_TEST_RABBITMQ_MANAGEMENT = 'http://localhost:15672/'

dotnet test src/Bifrost/Backend/Backend.IntegrationTests/Backend.IntegrationTests.csproj --configuration Release --filter 'FullyQualifiedName~Backend.IntegrationTests.Scenarios|FullyQualifiedName~PostgresRabbitMqPipelineTests'
```

Rapportscenarierne markeres som skipped, hvis en af de tre miljøvariable mangler. Når variablene er sat, skal utilgængelige servere eller fejl i scenarierne få testen til at fejle. Øvrige lokale tests kan køres uden disse servere.

GitHub Actions-workflowet `.github/workflows/ci.yml` starter PostgreSQL og RabbitMQ med management-plugin og sætter alle tre variabler, så de eksterne scenarier indgår i backendjobbets Release-kørsel med coverage. Testresultater og coverage uploades som artefakten `backend-test-results`.
