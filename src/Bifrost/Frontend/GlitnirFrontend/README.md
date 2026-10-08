# Glitnir frontend

Simpel adgangstest med tre sider:

- Forside: statisk indhold uden API-kald.
- Student: henter `/api/students/me` gennem Heimdall. Eir opretter en HttpOnly-cookie; siden viser identiteten fra API'et. Hent igen genbruger den samme cookie.
- Teacher: henter `/api/teachers/me` gennem Heimdall. Uden login vises HTTP 401. En konto uden Teacher-rollen får HTTP 403.

Login øverst til højre starter Heimdalls OIDC-flow hos VarService og vender tilbage til `/teacher`. Ingen adgangskoder eller tokens gemmes i frontend.

Med Aspire åbnes appen på http://localhost:7000. Ved separat `npm start` åbnes den på http://localhost:7002. Brug localhost for at dele login-cookies med Heimdall.
Heimdall forventes på port 6004 og VarService på port 6003.

API-proxyen konfigureres i `proxy.conf.json`; login-adressen i `heimdall.service.ts`. Tilladte returadresser findes i Heimdalls `Frontend`-konfiguration.
Proxyen virker med udviklingsserveren. Ved hosting skal `/api` routes gennem Heimdall.

`npm run build` bygger appen. `npm test -- --watch=false --browsers=ChromeHeadless` kører tests.

Browser-regression fra repository-roden, med AppHost og Chrome:

```powershell
npm.cmd install --prefix artifacts/login-browser --no-save --package-lock=false playwright-core
node scripts/Bifrost-JPF/lib/glitnir-browser-test.cjs
```

Testen kontrollerer den passive forside, elevcookien, Teacher 401 og login med retur til Teacher 200.
