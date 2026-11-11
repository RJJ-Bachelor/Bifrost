# Coverage

Fra repositoryets rod i PowerShell:

```powershell
.\scripts\Test\coverage\coverage.cmd
```

Scriptet kører backendens unit- og integrationstests, samler coverage og åbner
`Test/coverage/index.html`. Det rydder tidligere resultater i `Test/coverage` før
kørslen. Første gang installeres ReportGenerator, hvis værktøjet mangler.

Kun testkørslen, uden at generere HTML-rapport:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test\coverage\backend-tests.ps1 -ResultsDirectory .\Test\coverage\TestResults
```

Scripts finder projektfiler og `coverage.runsettings` ud fra deres egen placering,
så de kan også startes fra en anden arbejdsmappe. En relativ `ResultsDirectory`
fortolkes i forhold til terminalens aktuelle mappe.
