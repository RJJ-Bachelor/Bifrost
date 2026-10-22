# Bifrost i Bruno

Åbn `scripts/Bifrost-JPF` som collection i Bruno, og vælg **Local** og **Developer Mode**.
Collectionens fælles response-script gemmer værdier automatisk ved vellykkede kald:

| Kald | Variabler til næste kald |
|---|---|
| CreateSession / JoinSession / GetSession | `sessionId`, `sessionGeneration` og de sessionskoder, svaret indeholder |
| Student CreateHelpRequest / GetHelpRequestById / besked og status | `studentHelpRequestId` samt `helpRequestId` som alias |
| Teacher TakeNextHelpRequest / GetHelpRequest / svar og status | `teacherHelpRequestId`; en FAQ gemmer også `generalizationId` |
| ClearSession | Ny `sessionGeneration`; gamle anmodnings- og generaliserings-ID'er ryddes |
| DeleteSession | Den valgte session og dens anmodnings-ID'er ryddes |

Værdierne sættes både som runtime-variabler og i det valgte miljø, så de kan bruges
med det samme og ses under Local. Student-kald bruger `studentHelpRequestId`,
teacher-kald bruger `teacherHelpRequestId`. En FAQ-reservation overskriver derfor ikke
elevens ID til AcceptFaq/RejectFaq. CreateHelpRequest vælger også den første anmodning
til underviserens inspektion; TakeNextHelpRequest vælger derefter den faktiske reservation.
Listekald vælger ikke automatisk et vilkårligt element.

## Development

FAQ-testen bruger **Development** til data og inspektion og **MimirService** til publicering:

1. `Development/Seed FAQ demo` opretter en ny session med studerende og HelpRequests.
2. `MimirService/Publish generalization (dummy data)` sender det redigerbare JSON-payload via Mimirs test-API.
3. `Development/Get teacher queue`, `Get first student request` og `Get second student request` viser resultatet.
4. `Development/Take next FAQ` og `Answer FAQ` reserverer og besvarer fællesspørgsmålet.
5. `Development/Accept first student FAQ` og `Reject second student FAQ` afprøver elevernes to svarmuligheder med hver sin cookie.

Seed-kaldet udfylder session, generation,
generaliserings-id og de to helprequest-id'er som `faqDemo…`-runtime-variabler og kopierer
de almindelige variabler til det valgte miljø. Publish stopper i Bruno med en konkret fejl,
hvis payloadets værdier mangler eller ikke kan opløses. Eir føjer det fælles spørgsmål til de tilknyttede
samtaler som en systembesked fra `mimir` og opretter en FAQ i underviserens kø.
**Development → Create digital help request (new session)** opretter en almindelig
digital elevanmodning i en ny session og starter Mimirs normale behandling.
Manuelle anmodninger i en eksisterende session ligger under
`EirService/Student/HelpRequests/CreateHelpRequest`.

Kør først `Login/Teacher/Login` som normalt. Alle Development-kald bruger `gatewayUrl` og collectionens eksisterende cookie-login. Antallet af demostuderende sættes direkte som et tal i seed-kaldets JSON-body: `"studentCount": 6` (2–20). Demoens student-kald vælger den relevante demostuderende i Brunos cookiejar og bevarer teacher-login.
Se [Development-vejledningen](Development/README.md) for opsætning og hele forløbet.

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
│   │   └── GetStudentSession/
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
| `sessionId` | Kopiér `id` fra CreateSession eller GetSessions |
| `sessionCode` | Kopiér `code` fra CreateSession eller GetSession som teacher |
| `teacherSessionCode` | Kopiér `teacherCode` fra CreateSession eller GetSession som teacher |
| `sessionGeneration` | Kopiér `currentGeneration` fra et session-svar; starter på 1 |
| `helpRequestId` | Kopiér `id` fra CreateHelpRequest eller TakeNextHelpRequest |
| `generalizationId` | ID'et for det fælles FAQ-spørgsmål; udfyldes af seed/publish |
| `firstHelpRequestId` / `secondHelpRequestId` | De to oprindelige elevanmodninger fra seed |

Variablerne udfyldes manuelt ved normale kald. `Development/Seed FAQ demo` udfylder dem i det valgte miljø for testsessionen. Efter ClearSession opdateres `sessionGeneration` med den nye værdi fra svaret.

## Teacher

| Request | Metode og route |
|---|---|
| GetTeacherIdentity | `GET /api/teachers/me` |
| JoinTeacherSession | `POST /api/teachers/sessions/join` med underviserkode |
| CreateSession | `POST /api/teachers/sessions` med titel |
| GetSessions | `GET /api/teachers/sessions` |
| GetSession | `GET /api/teachers/sessions/{{sessionId}}` |
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
| CreateHelpRequest | `POST /api/students/sessions/{{sessionId}}/requests` med `message` og `helpType` |
| GetHelpRequests | `GET /api/students/requests` |
| GetHelpRequestsForSession | `GET /api/students/requests?sessionId={{sessionId}}` |
| GetHelpRequestById | `GET /api/students/requests/{{studentHelpRequestId}}` |
| GetStudentQueue | `GET /api/students/sessions/{{sessionId}}/queue` |
| AddStudentMessage | `POST /api/students/requests/{{studentHelpRequestId}}/messages` med `text` |
| CancelHelpRequest | `PATCH /api/students/requests/{{studentHelpRequestId}}/status` med `action: "Cancel"` |
| GetStudentFaqs | `GET /api/students/sessions/{{sessionId}}/faqs` |
| AcceptFaq / RejectFaq | `PATCH /api/students/requests/{{studentHelpRequestId}}/faq` med `accept: true` / `false` |

## FAQ og generalisering

**Teacher → KnowledgeBase → GetTeacherFaqs** viser både ubesvarede og besvarede fællesspørgsmål.
**Student → KnowledgeBase → GetStudentFaqs** viser kun besvarede spørgsmål i sessionens aktuelle generation.
Begge bruger `sessionId` fra Local og den relevante rolle-cookie.

Underviseren reserverer en FAQ gennem **Teacher → HelpRequests → TakeNextHelpRequest**
og besvarer den gennem **AnswerHelpRequest**. `teacherHelpRequestId` sættes automatisk til
den returnerede FAQ's ID, som også er `generalizationId`. Der bruges de samme endpoints som til almindelig digital hjælp.

Eleven svarer gennem **Student → HelpRequests → RespondToFaq → AcceptFaq / RejectFaq**.
Her er `studentHelpRequestId` elevens **oprindelige anmodning**, og elevcookien skal tilhøre den anmodning.
Accept afslutter anmodningen; afvisning sender den tilbage i køen. Svaret kræver status
`AwaitingConfirmation`, og hver elev vælger én af mulighederne.

Til hele seed-forløbet bruges Development-kaldene ovenfor: de vælger ID'er og elevcookies automatisk.
FAQ-listerne ligger under de almindelige EirService-mapper. Det genbrugelige FAQ-svar leveres
asynkront til KnowledgeBase, så studentens liste kan være tom lige efter besvarelsen.

## Kø og samtaler

CreateHelpRequest får ID, generation, `requestNumber` og `displayNumber` fra serveren.
Displaynummeret er fx `BF-001` og følger anmodningen ved returnering og genåbning.
Numrene starter forfra i hver session og efter ClearSession. Brug fortsat GUID'en
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
