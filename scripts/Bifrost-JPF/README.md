# Bifrost i Bruno

Åbn `scripts/Bifrost-JPF` som collection i Bruno, og vælg **Local**.
Til teacher-login skal du vælge **Developer Mode** via skjoldikonet øverst til højre.

## Login

- **Student:** Kør `Login/Student/Login`. Eir opretter automatisk en student-cookie,
  hvis den mangler. Bruno gemmer cookien og genbruger samme identitet.
- **Teacher:** Kør `Login/Teacher/Login`. Scriptet logger ind hos VarService og
  gemmer teacher-cookien i Bruno. Derefter kan teacher-endpoints køres direkte.

`teacherUsername` og `teacherPassword` er allerede sat i collectionens variabler
til de fungerende udviklingsværdier. Du kan også sætte dem i Local. Et udfyldt
password i Local har forrang; et tomt password falder tilbage til collectionens værdi.
CLI kan desuden bruge `BIFROST_TEACHER_PASSWORD` som process-variabel.

## Requests

Alle Eir-requests går gennem Heimdall på `http://localhost:6004`.
VarService ligger på `http://localhost:6003`. Adresserne kan ændres i variablerne.
Requests bruger automatisk Brunos gemte cookies.

| Mappe | Requests |
| --- | --- |
| `EirService/Students` | `Me`, `CreateHelpRequest`, `Requests` |
| `EirService/Teacher` | `Me`, `CreateSession` |
| `EirService/Monitoring` | `Alive` |
| `Heimdall` | `Health`, `Alive`, `OpenApi` |
| `VarService` | `Discovery` |
| `Session` | `Logout` |

`CreateSession` kalder `POST /api/teachers/createsession` uden body og returnerer
JSON-værdien `true`. Endpointet kræver login og Teacher-rollen.

Studentens `CreateHelpRequest` kalder `POST /api/students/createhelprequest` med
`id` og `message` i JSON-body og bruger et nyt GUID som id ved hver afsendelse.
Studentens identitet kommer fra cookien. Valideringsfejl returneres som HTTP 400.
`Requests` viser historikken for den aktuelle student.
`Session/Logout` logger teacher ud. Kør teacher-login igen, når sessionen udløber.

## Start services

Start Docker Desktop, og kør fra repository-roden:

```powershell
aspire.cmd start --apphost src/Bifrost/Orchestration/YggdrasilService/YggdrasilService.AppHost/YggdrasilService.AppHost.csproj --launch-profile http --non-interactive
aspire.cmd wait varservice --timeout 60 --non-interactive
aspire.cmd wait heimdallgateway --timeout 60 --non-interactive
```

## CLI

Fra `scripts/Bifrost-JPF`:

```powershell
npm.cmd ci
npm.cmd run validate
npm.cmd test
```

CLI sender de simple requests sekventielt: Login → EirService → Heimdall →
VarService → Session. Logout ligger sidst. Collectionen har ingen test-assertions.

## Browser login regression

Bruno's cookie jar does not apply Chrome's SameSite rules. This test runs the real
Angular login button in an isolated Chrome session, checks that only Bifrost login
is offered, verifies the return to port 7001 and calls the Teacher API through both
Heimdall and the Angular proxy.

With Bifrost running via Aspire and Chrome installed, run from the repository root:

```powershell
npm.cmd install --prefix artifacts/login-browser --no-save --package-lock=false playwright-core
node scripts/Bifrost-JPF/lib/browser-login-test.cjs
```

Optional process variables: `BIFROST_FRONTEND_URL`, `BIFROST_GATEWAY_URL`,
`BIFROST_TEACHER_USERNAME`, `BIFROST_TEACHER_PASSWORD`.
Defaults use localhost ports 7001/6004 and the development seed account.
Development uses SameSite=Lax for the VarService authentication and session cookies
so that Chrome accepts them on local HTTP.
