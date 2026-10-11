# Bifrost i Bruno

Åbn `scripts/Bifrost-JPF` som collection i Bruno, og vælg **Local**.
De nye session-requests er almindelige GET- og POST-kald med URL og eventuel JSON-body.
Der er ingen tests eller automatisk opsamling af session-værdier i de nye requests.

## Login

- **Student:** Kør `Login/Student/Login`. Bruno gemmer elevcookien og genbruger identiteten.
- **Teacher:** Vælg **Developer Mode** via skjoldikonet, og kør `Login/Teacher/Login`.
  Den eksisterende login-helper gemmer teacher-cookien i Bruno.

`teacherUsername` og `teacherPassword` følger det eksisterende setup.
Et udfyldt password i Local har forrang; et tomt password bruger collectionens værdi.
Alle Eir-requests går gennem Heimdall på `{{gatewayUrl}}`, som normalt er `http://localhost:6004`.

## Session-variabler i Local

| Variabel | Værdi |
|---|---|
| `sessionId` | Kopiér `id` fra CreateSession eller GetSessions |
| `sessionCode` | Kopiér `code` fra CreateSession eller GetSession som teacher |
| `sessionGeneration` | Kopiér `currentGeneration` fra et session-svar; starter på 1 |

Variablerne udfyldes manuelt. Efter ClearSession opdateres `sessionGeneration` med den nye værdi fra svaret.

## Teacher

| Request | Metode og route |
|---|---|
| Me | `GET /api/teachers/me` |
| CreateSession | `POST /api/teachers/sessions` med titel |
| GetSessions | `GET /api/teachers/sessions` |
| GetSession | `GET /api/teachers/sessions/{{sessionId}}` |
| ClearSession | `POST /api/teachers/sessions/{{sessionId}}/clear` med forventet generation |

## Students

| Request | Metode og route |
|---|---|
| Me | `GET /api/students/me` |
| JoinSession | `POST /api/students/sessions/join` med sessionskode |
| GetStudentSessions | `GET /api/students/sessions` |
| GetSession | `GET /api/students/sessions/{{sessionId}}` |
| CreateHelpRequest | `POST /api/students/createhelprequest` med besked, session-id og generation |
| Requests | `GET /api/students/requests` |
| SessionRequests | `GET /api/students/requests?sessionId={{sessionId}}` |

CreateHelpRequest bruger Brunos indbyggede `{{$guid}}` til et nyt request-id ved hver afsendelse.

Et almindeligt forløb er: teacher-login → CreateSession → udfyld Local → student-login → JoinSession → CreateHelpRequest → SessionRequests.
Kør derefter ClearSession som teacher, opdater generationen, og læs SessionRequests igen.

Monitoring, Heimdall, VarService og Logout ligger fortsat i deres eksisterende mapper.

## Start services

Start Docker Desktop, og kør fra repository-roden:

```powershell
aspire.cmd start --apphost src/Bifrost/Orchestration/YggdrasilService/YggdrasilService.AppHost/YggdrasilService.AppHost.csproj --launch-profile http --non-interactive
aspire.cmd wait varservice --timeout 60 --non-interactive
aspire.cmd wait heimdallgateway --timeout 60 --non-interactive
```
