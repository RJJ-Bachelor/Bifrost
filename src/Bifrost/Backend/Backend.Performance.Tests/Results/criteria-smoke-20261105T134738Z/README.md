# Smoke med de aktuelle K1–K3

Kørsel `20261105T134738793Z-43538d`, 5. november 2026. Hovedmålingen begyndte kl. 13:48:16 UTC (14:48:16 dansk tid). Kravversion: `eir-registration-1s-notifications-10s-v1`.

Profil: 10 studerende, tre aktive, én lektion og tre undervisere. Separat warm-up med to studerende i 10 sekunder; derefter 10 sekunders ramp-up, 30 sekunders steady-state og fem sekunders ramp-down. Studentpause to sekunder, underviserpause ét sekund, ±20 % variation. Hovedmålingen inklusive afslutning varede cirka 49,46 sekunder.

| Krav | Prøver | Maksimum | Grænse | Resultat |
|---|---:|---:|---:|---|
| K1: Oprettelses-POST → færdigt HTTP-svar | 35 | 482,5974 ms | 1.000 ms | bestået |
| K2: Første POST i frisk kø → første matchende modtagelse hos en af tre undervisere via SignalR | 1 | 665,4236 ms | 10.000 ms | bestået |
| K3: Digital svar-POST uden FAQ → matchende SignalR-besked hos anmodningens ejer | 37 | 1.888,5969 ms | 10.000 ms | bestået |

Alle tre krav har nul fejl, nul overskridelser og nul annullerede prøver. K2 har forventet `N = Lessons = 1`; senere overgange fra tom kø er ikke dækket. K3 er korreleret med præcist request-ID og dokumenterer ikke fysisk hjælp eller kanoniske FAQ-broadcasts.

Der blev registreret **2.313 afsluttede HTTP-kald, nul HTTP-fejl og 1.459 SignalR-beskeder**. Maksimum var 13 faktiske forbindelser (10 studerende og tre undervisere); alle 13 tilkoblinger lykkedes, 13 lukninger var planlagte, og sluttallet var nul. Der var ingen uventede lukninger eller reconnects. To øvrige GET-kald blev annulleret af klienten og er bevaret som diagnostiske annulleringer, adskilt fra kravprøverne.

Miljø: frisk lokal Yggdrasil/Aspire-opstart i Development, backend i Debug på Windows, generator i Release med .NET 10.0.14, PostgreSQL og RabbitMQ i lokal Docker samt `Generalization=Fake`. Kun generatorens ressourcer blev målt (PID 28464); der er ingen backendressourcemålinger i dette snapshot. Ingen browserklienter var aktiveret; browserdiagnostik er valgfri.

[Rapport og grafer](report.html), [rådata](raw.json), [krav og oversigt](summary.json), [operationer](operations.csv), [forbindelser](connections.csv).

Filerne er direkte kopier af denne kørsels målinger. Warm-up-data, infrastrukturlogs og loginoplysninger er ikke medtaget. Prøven verificerer måleopsætningen og de observerede krav ved **10 studerende**; den dokumenterer ikke kapacitet eller beståede krav ved 1.000 studerende.
