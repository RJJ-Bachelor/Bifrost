# Kontrolprøve efter leveringsrettelser, 5. november 2026

Kørsels-ID: `20261105T092542092Z-8100f5`.

100 studerende, ti aktive, tre undervisere og tre lektioner. Separat warm-up:
to studerende i 10 sekunder. Måleprofil: 30 sekunders ramp-up, 60 sekunders
steady-state og 10 sekunders ramp-down. Ingen browserklienter.

10.968 HTTP-kald uden fejl; 99 korrelerede notifikationsmålinger uden timeout.
Alle 103 tilkoblinger lykkedes, populationskontrollen fandt 100 unikke studerende,
og alle forbindelser blev lukket planlagt. Ingen fejlede observationer.

[Åbn rapporten](report.html). [Analyse, tidsgrænser og begrænsninger](../performance-improvements.md)
beskriver også de tidligere fejlede prøver. Dette er en kontrol med 100 studerende,
ikke en verificering ved 1.000 eller af fuld P3. Et Eir-testbuild overlappede de
sidste cirka 20 sekunder; CPU-forbruget er derfor ikke målt isoleret. TCP-proben
begyndte først efter afsluttet måling.

`raw-data.zip` indeholder præcis disse fem oprindelige filer:

- `raw.json`: 11.193 operationsobservationer, 3.599 modtagne notifikationer,
  206 forbindelseshændelser og 109 belastnings-/ressourcesnapshots.
- `operations.csv` og `notifications.csv`.
- `measured-sessions.json`: tre testsessions.
- `measured-students.json`: 100 syntetiske studentidentiteter.

Pak arkivet ud i denne mappe for at bruge rapportens links til rå JSON og CSV.
De øvrige oversigter, forbindelser, ressourcer, belastningskurver og `queues.csv`
ligger allerede udpakket. `queues.csv` er diskrete observationer fra kømonitoren;
ingen logs, autentifikationsdata, traces eller browserlagring er kopieret med.
