# Bifrost i Bruno

Åbn `scripts/Bifrost-JPF` som collection i Bruno, og vælg **Local** og **Developer Mode**.
Collectionens fælles response-script gemmer værdier automatisk ved vellykkede kald:

| Kald | Variabler til næste kald |
|---|---|
| CreateSession / JoinSession / GetSession | `sessionId`, `sessionGeneration` og de sessionskoder, svaret indeholder |
| Student CreateHelpRequest / GetHelpRequestById / besked og status | `studentHelpRequestId` samt `helpRequestId` som alias |
| Teacher TakeNextHelpRequest / GetHelpRequest / svar og status | `teacherHelpRequestId`; en FAQ gemmer også `generalizationId` |
| ArchiveSession | `archiveId` og ny `sessionGeneration`; gamle anmodnings- og demo-ID'er ryddes |
| ClearSession | Ny `sessionGeneration`; gamle anmodnings- og generaliserings-ID'er ryddes |
| DeleteSession | Den valgte session og dens anmodnings-ID'er ryddes |

Værdierne sættes både som runtime-variabler og i det valgte miljø, så de kan bruges
med det samme og ses under Local. Student-kald bruger `studentHelpRequestId`,
teacher-kald bruger `teacherHelpRequestId`. En FAQ-reservation overskriver derfor ikke
elevens ID til AcceptFaq/RejectFaq. CreateHelpRequest vælger også den første anmodning
til underviserens inspektion; TakeNextHelpRequest vælger derefter den faktiske reservation.
Listekald vælger ikke automatisk et vilkårligt element.

## Development

**Development er valgfri.** De almindelige sessions-, arkiv-, kø- og samtalekald ligger
under **EirService** og kan bruges uden seed eller Development-mappen.
Testhjælperne er samlet efter deres formål:

| Mappe | Brug |
|---|---|
| Development → FAQ demo | Opret testdata, og afprøv en fælles FAQ med to forskellige studerende |
| Development → Workflows | Opret en ny session og en digital HelpRequest gennem de normale endpoints |
| MimirService | Send en testgeneralisering via Mimir og RabbitMQ, med seed-data eller egne data |

Kun **Seed FAQ demo** kalder Eirs særlige Development-endpoint. Demoens øvrige kald
bruger de normale endpoints med sikre demo-ID'er og de rigtige studentercookies.
Workflows-kaldet automatiserer almindeligt login, sessionsoprettelse, tilmelding og oprettelse af en anmodning.
Mimirs to testendpoints og Eirs seed findes kun i service-miljøet `Development` og kræver teacher-login.

Til eksisterende data bruges **MimirService → Generalize own help requests**.
Opret mindst to HelpRequests i samme session og generation, og indsæt deres ID'er
i requestens JSON-body. Seed er valgfri, og `sessionId` og `sessionGeneration` følger de almindelige sessionsvariabler.
Se [forløbet med egne data](Development/README.md#generalisering-af-egne-helprequests)
eller [den komplette FAQ-demo](Development/README.md#kom-i-gang).

## Mapper

EirService følger API-lagets rolle- og modulmapper. Elevens læsekald til
HelpRequests ligger samlet i én mappe:

```text
EirService/
├── Monitoring/
│   └── Alive/
├── Student/
│   ├── Participants/
│   │   ├── GetStudentIdentity/
│   │   ├── JoinSession/
│   │   └── GetStudentSessions/
│   ├── Sessions/
│   │   ├── GetStudentSession/
│   │   ├── GetSessionArchives/
│   │   └── GetSessionArchive/
│   ├── HelpRequests/
│   │   ├── CreateHelpRequest/
│   │   ├── GetHelpRequests/
│   │   │   ├── GetHelpRequests.yml
│   │   │   ├── GetHelpRequestsForSession.yml
│   │   │   └── GetHelpRequestById.yml
│   │   ├── GetStudentQueue/
│   │   ├── AddStudentMessage/
│   │   ├── CancelHelpRequest/
│   │   └── RespondToFaq/
│   │       ├── AcceptFaq.yml
│   │       └── RejectFaq.yml
│   └── KnowledgeBase/
│       └── GetStudentFaqs/
└── Teacher/
    ├── Participants/
    │   ├── GetTeacherIdentity/
    │   └── JoinSession/
    ├── Sessions/
    │   ├── CreateSession/
    │   ├── GetSessions/
    │   ├── GetSession/
    │   ├── ArchiveSession/
    │   ├── GetSessionArchives/
    │   ├── GetSessionArchive/
    │   ├── ClearSession/
    │   └── DeleteSession/
    ├── HelpRequests/
    │   ├── GetQueue/
    │   ├── GetHelpRequest/
    │   ├── TakeNextHelpRequest/
    │   ├── AnswerHelpRequest/
    │   └── ChangeHelpRequestStatus/
    └── KnowledgeBase/
        └── GetTeacherFaqs/
```

`GetHelpRequests`-mappen samler listen, listen filtreret på en session og én samtale
via ID. Listekaldene viser dine anmodninger i sessionernes aktuelle generation.
`GetHelpRequestById` henter status og alle samtalens beskeder og kan også bruges
til en anmodning fra før ClearSession.
Teacher-sessioner returnerer opretterens ID som `createdByTeacherId`.
Andre undervisere kan bruge `Teacher/Participants/JoinSession` med `teacherSessionCode`.
`sessionCode` er kun til studerende; undervisertilmelding kræver den separate `teacherCode`.
`GetSessions` viser derefter både oprettede og tilmeldte sessioner. Tilknyttede
undervisere deler køen, mens hver reservation tilhører den underviser, der tog den.
Hver session i `GetSessions` har `waitingCount` for sin aktuelle generation, også når tallet er nul.
Det svarer til `GetQueue`: alle `Pending`-anmodninger, inklusive reserverede anmodninger og FAQ-spørgsmål.
Anmodninger med `Active`, `AwaitingFaq` eller `AwaitingConfirmation` tælles ikke med.

## Login

- **Student:** Første student-kald gennem Heimdall opretter automatisk elevcookien.
  `Login/Student/Login` kan stadig bruges til at se identiteten; Bruno gemmer cookien og genbruger den.
- **Teacher:** Vælg **Developer Mode** via skjoldikonet, og kør `Login/Teacher/Login`.
  Den eksisterende login-helper gemmer teacher-cookien i Bruno.

`teacherUsername` og `teacherPassword` følger det eksisterende setup.
Et udfyldt password i Local har forrang; et tomt password bruger collectionens værdi.
De almindelige Eir-requests går gennem Heimdall på `{{gatewayUrl}}`, som normalt er `http://localhost:6004`.

## Session-variabler i Local

| Variabel | Værdi |
|---|---|
| `sessionId` | Gemmes automatisk fra CreateSession, JoinSession og GetSession |
| `sessionCode` | Gemmes fra CreateSession/GetSession som teacher og studentens JoinSession |
| `teacherSessionCode` | Gemmes fra CreateSession/GetSession som teacher og underviserens JoinSession |
| `sessionGeneration` | Den aktuelle generation; opdateres efter ArchiveSession og ClearSession |
| `archiveId` | Gemmes fra ArchiveSession og arkivdetaljer; vælg et ældre arkiv med dets ID i detaljekaldets URL |
| `studentHelpRequestId` / `helpRequestId` | Elevens anmodning; gemmes fra elevens oprette-, læse- og ændringskald |
| `teacherHelpRequestId` | Gemmes fra underviserens læse-, reservations- og ændringskald |
| `generalizationId` | ID'et for det fælles FAQ-spørgsmål; gemmes fra seed, begge Mimir-testkald og FAQ-svar |
| `firstHelpRequestId` / `secondHelpRequestId` | De to oprindelige elevanmodninger fra seed |

Variablerne udfyldes automatisk i runtime og det valgte miljø ved vellykkede kald.
For en eksisterende frontend-session kan GetSession kaldes med dens konkrete ID i URL'en
én gang; svaret vælger sessionen og generationen til de efterfølgende kald. Et vellykket
**Generalize own help requests** vælger også automatisk sessionen fra det indsendte payload.

## Teacher

| Request | Metode og route |
|---|---|
| GetTeacherIdentity | `GET /api/teachers/me` |
| JoinTeacherSession | `POST /api/teachers/sessions/join` med underviserkode |
| CreateSession | `POST /api/teachers/sessions` med titel |
| GetSessions | `GET /api/teachers/sessions` |
| GetSession | `GET /api/teachers/sessions/{{sessionId}}` |
| ArchiveSession | `POST /api/teachers/sessions/{{sessionId}}/archive` med forventet generation |
| GetSessionArchives | `GET /api/teachers/sessions/{{sessionId}}/archives` |
| GetSessionArchive | `GET /api/teachers/sessions/{{sessionId}}/archives/{{archiveId}}` |
| ClearSession | `POST /api/teachers/sessions/{{sessionId}}/clear` med forventet generation |
| DeleteSession | `DELETE /api/teachers/sessions/{{sessionId}}` |
| GetQueue | `GET /api/teachers/sessions/{{sessionId}}/queue` |
| TakeNextHelpRequest | `POST /api/teachers/sessions/{{sessionId}}/queue/take-next` |
| GetHelpRequest | `GET /api/teachers/requests/{{teacherHelpRequestId}}` |
| AnswerHelpRequest | `POST /api/teachers/requests/{{teacherHelpRequestId}}/messages` med `text` |
| Start / Complete / Cancel | `PATCH /api/teachers/requests/{{teacherHelpRequestId}}/status` med `action` |
| GetTeacherFaqs | `GET /api/teachers/sessions/{{sessionId}}/faqs` |

## Student

| Request | Metode og route |
|---|---|
| GetStudentIdentity | `GET /api/students/me` |
| JoinSession | `POST /api/students/sessions/join` med sessionskode |
| GetStudentSessions | `GET /api/students/sessions` |
| GetStudentSession | `GET /api/students/sessions/{{sessionId}}` |
| GetSessionArchives | `GET /api/students/sessions/{{sessionId}}/archives` |
| GetSessionArchive | `GET /api/students/sessions/{{sessionId}}/archives/{{archiveId}}` |
| CreateHelpRequest | `POST /api/students/sessions/{{sessionId}}/requests` med `message` og `helpType` |
| GetHelpRequests | `GET /api/students/requests` |
| GetHelpRequestsForSession | `GET /api/students/requests?sessionId={{sessionId}}` |
| GetHelpRequestById | `GET /api/students/requests/{{studentHelpRequestId}}` |
| GetStudentQueue | `GET /api/students/sessions/{{sessionId}}/queue` |
| AddStudentMessage | `POST /api/students/requests/{{studentHelpRequestId}}/messages` med `text` |
| CancelHelpRequest | `PATCH /api/students/requests/{{studentHelpRequestId}}/status` med `action: "Cancel"` |
| GetStudentFaqs | `GET /api/students/sessions/{{sessionId}}/faqs` |
| AcceptFaq / RejectFaq | `PATCH /api/students/requests/{{studentHelpRequestId}}/faq` med `accept: true` / `false` |

## Arkivering

1. Log ind som underviser, og vælg sessionen med **Teacher → Sessions → GetSession**.
2. Kør **ArchiveSession**. Bodyens `expectedGeneration` kommer fra `sessionGeneration`.
   Svaret gemmer `archiveId`, opdaterer den aktuelle generation og rydder gamle anmodnings-ID'er.
3. Brug **GetSessionArchives** og **GetSessionArchive** under Teacher eller Student → Sessions.
   For et ældre arkiv erstattes `{{archiveId}}` én gang i detaljekaldets URL med listens `id`;
   svaret gemmer det valgte `archiveId`. Listekald vælger aldrig automatisk et arkiv.
4. Kontrollér den nye generation med **GetSession** og den tomme kø med **GetQueue**.
   Den samme session og de eksisterende sessionskoder bruges videre.

Et arkiv indeholder generationens FAQ'er og samtaler. Underviseren kan hente alle
generationens samtaler; den studerende kan hente sine egne samtaler og besvarede FAQ'er.
Arkivlisten indeholder `privateConversationCount` og `generalizedQuestionCount` for hver generation.
Det første tæller private samtaler (HelpRequests), så hver samtale tælles én gang uanset antallet af beskeder.
Det andet tæller generationens generaliserede FAQ-spørgsmål. Underviseren ser alle spørgsmål,
mens den studerendes tal kun omfatter egne samtaler og besvarede FAQ-spørgsmål.
Arkivdetaljer ændrer aldrig `sessionGeneration` til den gamle generation.
Arkivering sender samtidig besked til Mimir gennem den normale outbox og RabbitMQ.
Hvis en anden underviser allerede har skiftet generation, giver ArchiveSession `409`;
hent GetSession igen før et nyt forsøg.

**ClearSession** findes fortsat som den særskilte ryd-funktion uden oprettelse af et arkiv.
Brug **ArchiveSession**, når generationen skal gemmes og kunne findes i arkivlisten.

## FAQ og generalisering

**Teacher → KnowledgeBase → GetTeacherFaqs** viser både ubesvarede og besvarede fællesspørgsmål.
**Student → KnowledgeBase → GetStudentFaqs** viser kun besvarede spørgsmål i sessionens aktuelle generation.
Begge bruger `sessionId` fra Local og den relevante rolle-cookie.

Underviseren reserverer en FAQ gennem **Teacher → HelpRequests → TakeNextHelpRequest**
og besvarer den gennem **AnswerHelpRequest**. `teacherHelpRequestId` sættes automatisk til
den returnerede FAQ's ID, som også er `generalizationId`. Der bruges de samme endpoints som til almindelig digital hjælp.

Eleven svarer gennem **Student → HelpRequests → RespondToFaq → AcceptFaq / RejectFaq**.
Her er `studentHelpRequestId` elevens **oprindelige anmodning**, og elevcookien skal tilhøre den anmodning.
Fysiske og digitale anmodninger kan begge få FAQ-svaret i deres samtale.
Accept afslutter anmodningen; afvisning sender den tilbage i køen med samme køtid og hjælpetype,
så en fysisk anmodning fortsat venter på fysisk hjælp. Svaret kræver status
`AwaitingConfirmation`, og hver elev vælger én af mulighederne.

Til hele seed-forløbet bruges **Development → FAQ demo**: kaldene vælger ID'er og elevcookies automatisk.
FAQ-listerne ligger under de almindelige EirService-mapper. Det genbrugelige FAQ-svar leveres
asynkront til KnowledgeBase, så studentens liste kan være tom lige efter besvarelsen.

## Kø og samtaler

CreateHelpRequest får ID, generation, `requestNumber` og `displayNumber` fra serveren.
Displaynummeret er fx `BF-001` og følger anmodningen ved returnering og genåbning.
Numrene starter forfra i hver session og efter ArchiveSession eller ClearSession. Brug fortsat GUID'en
som `helpRequestId`; køplaceringen er et separat tal.
Kø- og samtalekald ligger
under `EirService/Student/HelpRequests` og `EirService/Teacher/HelpRequests`.
Student-kald bruger elevcookien; teacher-kald bruger lærerlogin.
De kan bruges i samme cookiejar uden at logge læreren ud.
Start, Complete og Cancel ligger i lærerens `ChangeHelpRequestStatus`-mappe
og bruger samme endpoint. Teacher Cancel frigiver reservationen og sender
anmodningen forrest i køen med `Pending`. Student Cancel lukker anmodningen
med `Cancelled` og fjerner den fra køen.
Se [Development-vejledningen](Development/README.md) for FAQ-testforløbet.

Et almindeligt forløb er: teacher-login → CreateSession → student-login → JoinSession → CreateHelpRequest → GetHelpRequestsForSession.
Session-id, sessionskode og anmodnings-id følger automatisk med. ClearSession opdaterer også generationen automatisk.

Alle samtalebeskeder indeholder `messageType` som enum-streng:

| `messageType` | Betydning |
|---|---|
| `StudentQuestion` | Elevens oprindelige spørgsmål |
| `FollowUpQuestion` | Et efterfølgende spørgsmål fra eleven |
| `FaqQuestion` | Det generaliserede fællesspørgsmål fra Mimir |
| `FaqAnswer` | Underviserens fælles FAQ-svar, også når det kopieres til elevens samtale |
| `TeacherAnswer` | Underviserens direkte svar på elevens anmodning |

`authorRole` angiver fortsat afsenderens rolle. Beskedtypen fastlægges af serveren;
request-bodyen bruger fortsat `message` eller `text`. Eksisterende beskeder får typer
via databasemigration ved næste opstart af EirService.

Monitoring, Heimdall, VarService og Logout ligger fortsat i deres eksisterende mapper.

## Start services

Start Docker Desktop, og kør fra repository-roden:

```powershell
aspire.cmd start --apphost src/Bifrost/Orchestration/YggdrasilService/YggdrasilService.AppHost/YggdrasilService.AppHost.csproj --launch-profile http --non-interactive
aspire.cmd wait varservice --timeout 60 --non-interactive
aspire.cmd wait heimdallgateway --timeout 60 --non-interactive
```

## Student-authentication

Heimdall udsteder `bifrost-anonymous-user` og sender den med til servicen allerede i det første kald.
Eir, Mimir og Gna validerer cookien; direkte student-kald uden en gyldig cookie får 401.
Student-login hos VarService er ikke nødvendigt.

Gamle Eir-cookies erstattes ved første kald efter denne ændring. Det giver et nyt student-ID,
så eksisterende session-medlemskaber skal oprettes igen med JoinSession.
