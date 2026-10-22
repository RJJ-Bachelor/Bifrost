# Bifrost i Bruno

Åbn `scripts/Bifrost-JPF` som collection i Bruno, og vælg **Local**.
De almindelige session-requests er GET- og POST-kald med URL og eventuel JSON-body.
`Development/Seed FAQ demo` opsamler session-værdier automatisk til testforløbet.

## Development

FAQ-testen bruger **Development** til data og inspektion og **MimirService** til publicering:

1. `Development/Seed FAQ demo` opretter en ny session med studerende og HelpRequests.
2. `MimirService/Publish generalization (dummy data)` sender det redigerbare JSON-payload via Mimirs test-API.
3. `Development/Get teacher queue`, `Get first student request` og `Get second student request` viser resultatet.

Seed-kaldet udfylder session, generation,
generaliserings-id og de to helprequest-id'er som `faqDemo…`-runtime-variabler og kopierer
de almindelige variabler til det valgte miljø. Publish stopper i Bruno med en konkret fejl,
hvis payloadets værdier mangler eller ikke kan opløses. Eir føjer det fælles spørgsmål til de tilknyttede
samtaler som en systembesked fra `mimir` og opretter en FAQ i underviserens kø.
**Development → Create digital help request (new session)** opretter en almindelig
digital elevanmodning i en ny session og starter Mimirs normale behandling.
Manuelle anmodninger i en eksisterende session ligger under
`EirService/Student/HelpRequests/CreateHelpRequest`.

Kør først `Login/Teacher/Login` som normalt. Alle Development-kald bruger `gatewayUrl` og collectionens eksisterende cookie-login. Antallet af demostuderende sættes direkte som et tal i seed-kaldets JSON-body: `"studentCount": 6` (2–20). De to student-kald vælger hver sin demostuderende i Brunos cookiejar og bevarer teacher-login.
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
│   └── HelpRequests/
│       ├── CreateHelpRequest/
│       ├── GetHelpRequests/
│       │   ├── GetHelpRequests.yml
│       │   ├── GetHelpRequestsForSession.yml
│       │   └── GetHelpRequestById.yml
│       ├── GetStudentQueue/
│       ├── AddStudentMessage/
│       └── CancelHelpRequest/
└── Teacher/
    ├── Participants/
    │   ├── GetTeacherIdentity/
    │   └── JoinSession/
    ├── Sessions/
    │   ├── CreateSession/
    │   ├── GetSessions/
    │   ├── GetSession/
    │   └── ClearSession/
    └── HelpRequests/
        ├── GetQueue/
        ├── GetHelpRequest/
        ├── TakeNextHelpRequest/
        ├── AnswerHelpRequest/
        └── ChangeHelpRequestStatus/
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

Variablerne udfyldes manuelt ved normale kald. `Development/Seed FAQ demo` udfylder dem i det valgte miljø for testsessionen. Efter ClearSession opdateres `sessionGeneration` med den nye værdi fra svaret.

## Teacher

| Request | Metode og route |
|---|---|
| GetTeacherIdentity | `GET /api/teachers/me` |
| CreateSession | `POST /api/teachers/sessions` med titel |
| GetSessions | `GET /api/teachers/sessions` |
| GetSession | `GET /api/teachers/sessions/{{sessionId}}` |
| ClearSession | `POST /api/teachers/sessions/{{sessionId}}/clear` med forventet generation |

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
| GetHelpRequestById | `GET /api/students/requests/{{helpRequestId}}` |

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
Se [HelpRequests og testforløb](../../docs/help-requests.md).

Et almindeligt forløb er: teacher-login → CreateSession → udfyld Local → student-login → JoinSession → CreateHelpRequest → GetHelpRequestsForSession.
Kør derefter ClearSession som teacher, opdater generationen, og læs GetHelpRequestsForSession igen.

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
Student-login hos VarService er ikke nødvendigt. Se [authentication-flow og nøgleopsætning](../../docs/student-authentication.md).

Gamle Eir-cookies erstattes ved første kald efter denne ændring. Det giver et nyt student-ID,
så eksisterende session-medlemskaber skal oprettes igen med JoinSession.
