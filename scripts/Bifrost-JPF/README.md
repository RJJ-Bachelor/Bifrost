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
| `EirService/Students` | `Me`, `SendMessages`, `CreateRequest`, `Requests` |
| `EirService/Teacher` | `Me`, `SendMessages`, `CreateSession` |
| `EirService/Monitoring` | `Alive` |
| `Heimdall` | `Health`, `Alive`, `OpenApi` |
| `VarService` | `Discovery` |
| `Session` | `Logout` |

`CreateSession` kalder `POST /api/teachers/createsession` uden body og returnerer
JSON-værdien `true`. Endpointet kræver login og Teacher-rollen.

Studentens `CreateRequest` bruger et nyt GUID som id ved hver afsendelse.
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
