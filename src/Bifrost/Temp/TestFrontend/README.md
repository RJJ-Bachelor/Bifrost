# Bifrost testfrontend

Start backendservices og Heimdall på http://localhost:6004 og VarService på http://localhost:6003.
Kør `npm start` i denne mappe og åbn **http://localhost:7003**. Brug localhost (ikke 127.0.0.1), så login-cookies deles med Heimdall.

Faner: Oversigt (servicestatus), Elev (cookie-identitet og testbesked), Forespørgsler (egne forespørgsler og oprettelse), Lærer (login, identitet, testbesked og testsession).
Alle API-kald går via Angulars dev-proxy til Heimdall. API-svar og fejl vises i siden. Der bruges ingen dummydata som erstatning for fejl.

Login-knappen åbner Heimdalls OpenID Connect-login hos VarService og vender tilbage til lærerfanen. Lærerkontoen skal have rollen Teacher. Log ud afslutter lærersessionen; elevens cookie bevares.

Proxyens mål findes i `proxy.conf.json`, login-adressen i `heimdall.service.ts` og `app.component.html`. Frontend-returadressen tillades gennem Heimdalls `Frontend:Url`-konfiguration.
Ved andre porte skal adresserne opdateres samlet, inklusive VarServices tilladte OIDC callback-adresser.

`npm run build` bygger frontend. Dev-proxyen bruges kun af `npm start`; ved hosting skal `/api` routes gennem Heimdall.
