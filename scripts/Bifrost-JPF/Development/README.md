# Development i Bifrost-JPF

Åbn `scripts/Bifrost-JPF` som collection, vælg **Local** og **Developer Mode**, og udvid **Development**. Kaldene bruger `gatewayUrl` og samme login som resten af collectionen.

## Kom i gang

1. Start Heimdall, Eir og deres normale afhængigheder i `Development`. FAQ-flowet bruger også Mimir og RabbitMQ.
2. Kør **Login → Teacher → Login** som normalt. `teacherUsername` og `teacherPassword` bruges af den eksisterende login-helper; Bruno gemmer teacher-cookien.
3. Sæt `studentCount` direkte i JSON-bodyen til et tal mellem 2 og 20 (standard 6), og send **Development → Seed FAQ demo**. Forvent `201 Created`. Ved en fejl vises den almindelige HTTP-status og response body; sessionsvariabler ændres kun ved succes.
4. Åbn den nye session i test-frontenden med samme underviser. Svaret indeholder session-id, studenterkode og underviserkode. En anden underviser kan joine med underviserkoden.
5. Send **MimirService → Publish generalization (dummy data)**, når du vil sende et fælles spørgsmål om de to første HelpRequests via Mimir. JSON-bodyen kan redigeres direkte, og seed-kaldet har udfyldt ID'erne. Forvent `202 Accepted`. Beskeden leveres asynkront gennem den normale outbox og RabbitMQ.
6. Brug **Get teacher queue**, indtil FAQ'en står først med `isFaq: true` og uden en tildelt underviser. Genlevering med samme generaliserings-id tilføjer ikke spørgsmålet igen.
7. Kør **Take next FAQ** én gang og derefter **Answer FAQ** med samme teacher-login. Svaret kan redigeres direkte i JSON-bodyen. Answer-kaldet kontrollerer, at det reserverede ID er demoens FAQ.
8. **Get first student request** og **Get second student request** viser nu det generaliserede spørgsmål, underviserens svar og status `AwaitingConfirmation`.
9. Kør **Accept first student FAQ** og **Reject second student FAQ**. Kaldene vælger hver sin elevcookie og oprindelige anmodning. Første elev afsluttes med `Completed`, mens den anden vender tilbage til `Pending` i køen. Et nyt svar på samme anmodning giver `409`.
10. **EirService → Teacher → KnowledgeBase → GetTeacherFaqs** viser alle sessionens fællesspørgsmål. **EirService → Student → KnowledgeBase → GetStudentFaqs** viser de besvarede spørgsmål for den aktuelle elevcookie. Begge bruger `sessionId` fra Local; svaret kan først ses i KnowledgeBase, når svar-eventet er leveret.

De almindelige accept-/afvisningskald findes under **EirService → Student → HelpRequests → RespondToFaq**.
Her bruges `studentHelpRequestId`, som gemmes automatisk for den aktuelle elevs anmodning. Development-varianterne
vælger både ID og cookie automatisk til demoen.

Heimdall har to proxy-routes, som kun konfigureres i `Development`:

| Bruno-kald | Gateway-route | Service-route |
|---|---|---|
| Seed FAQ demo | `POST /api/teachers/development/seed` | Eir: `POST /api/test/seed` |
| MimirService/Publish generalization (dummy data) | `POST /api/teachers/development/generalizations` | Mimir: `POST /api/test/generalizations` |

Begge kræver Teacher-login. Heimdall videresender access token fra teacher-cookien som ved de øvrige underviserkald. Kø- og student-kald bruger de eksisterende routes. Eir og Mimir registrerer kun deres test-endpoints i `Development`.

Hvis Heimdall allerede kører, skal den indlæse den opdaterede Development-konfiguration. Genstart Heimdall, hvis testkaldene stadig svarer `404`. `401` betyder, at teacher-login mangler eller er udløbet; kør Login igen.

## Data og variabler

Seed-kaldets body er gyldig JSON uden en miljøvariabel for antallet:

```json
{
  "scenario": "faq-demo",
  "studentCount": 6
}
```

Ret tallet i bodyen, hvis du ønsker flere eller færre demostuderende.

Hver seed-kørsel opretter en ny session med nye studenterdeltagere og én HelpRequest pr. studerende. Der bruges både `Physical` og `Digital`. Eksisterende data bevares, og opstart seeder ikke data. FAQ'en sendes først, når du kører **MimirService → Publish generalization (dummy data)**.

Seed-servicen genbruger de eksisterende commands til sessioner, tilmelding og HelpRequests. Validering og beskedpublicering følger det normale flow. Hvis et senere command fejler, kan tidligere oprettede data fra kørslen være gemt; de kan slettes via den normale sessionsfunktion.

Ved `201` udfyldes `sessionId`, `sessionCode`, `teacherSessionCode`, `sessionGeneration`, `studentHelpRequestId`, `helpRequestId`, `generalizationId`, `firstHelpRequestId` og `secondHelpRequestId` både i runtime og i det valgte miljø. `teacherHelpRequestId` sættes ved reservationen. De øvrige EirService-kald kan derefter bruge testsessionen.

FAQ-demoen gemmer også sine ID'er og generation som særskilte `faqDemo…`-runtime-variabler.
Demo-kaldene bruger disse værdier, så de også virker, når
miljøvariabler ikke følger med til næste request. Cookies og `seedReady` gemmes også i runtime.
De normale EirService-kald opsamler også sessioner og anmodninger automatisk gennem
collectionens fælles response-script. Sessionsskift og ClearSession rydder gamle anmodnings-ID'er.

**Take next FAQ** gemmer det faktisk reserverede ID som `faqDemoTakenRequestId`.
Et nyt seed nulstiller værdien. **Answer FAQ** kræver, at dette ID matcher demoens generalisering.

Student-kaldene vælger den relevante demo-cookie i Brunos normale cookiejar med en lille helper, ligesom teacher-login gør. Et efterfølgende almindeligt student-kald bruger derfor den sidst valgte demostuderende. Teacher-cookien bevares. Ryd studenter-cookien i Bruno, hvis du vil have en ny anonym studenteridentitet.

Demostuderende i Bruno har egne identiteter. En studerende i browseren kan joine med studenterkoden og oprette egne spørgsmål, hvis du vil teste accept/afvisning i UI'et. Brug da disse HelpRequest-id'er i Mimir-payloadet.

I **MimirService → Publish generalization (dummy data)** redigeres payloadet direkte som JSON.
Samme generaliserings-id og payload kan sendes igen for at teste genlevering. Kør seed-kaldet
igen for at oprette et nyt, uafhængigt datasæt.
Ved egne data skal alle helprequest-id'er høre til den angivne session og dens aktuelle generation.
Ventende, ureserverede requests får spørgsmålet i samtalen og status `AwaitingFaq`.
Reserverede, aktive, afsluttede og annullerede requests ændres ikke. `202` bekræfter, at Mimir
har gemt eventet i outboxen; Eir validerer modtagerne ved den asynkrone behandling.

Hvis `generalizationId` tidligere gav en GUID-/JSON-fejl, genindlæs collectionen i Bruno og
kør **Seed FAQ demo** igen før Publish. Publish kontrollerer nu det opløste JSON-payload
og stopper i Bruno med en konkret besked, hvis ID'er eller andre nødvendige værdier mangler.
Der skal ikke kopieres GUID'er manuelt i det seedede forløb.

## Almindeligt Mimir-flow

**Development → Create digital help request (new session)** logger ind som underviser,
opretter en ny session, tilmelder den aktuelle studerende og sender en digital HelpRequest
til Eir. Mimir modtager `request.created` og behandler anmodningen gennem sit normale flow.
Dette er et separat forløb fra FAQ-demoen og opdaterer session-variablerne i Local.
Til manuelle kald i en eksisterende session bruges
`EirService/Student/HelpRequests/CreateHelpRequest/CreateDigitalRequest`.

Se [FAQ-endpoints og brug af ID'er](../README.md#faq-og-generalisering) for de almindelige student- og teacher-kald.
