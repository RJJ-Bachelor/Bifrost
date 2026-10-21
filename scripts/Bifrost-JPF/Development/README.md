# Development i Bifrost-JPF

Åbn `scripts/Bifrost-JPF` som collection, vælg **Local** og **Developer Mode**, og udvid **Development**. Kaldene bruger `gatewayUrl` og samme login som resten af collectionen.

## Kom i gang

1. Start Heimdall, Eir og deres normale afhængigheder i `Development`. FAQ-flowet bruger også Mimir og RabbitMQ.
2. Kør **Login → Teacher → Login** som normalt. `teacherUsername` og `teacherPassword` bruges af den eksisterende login-helper; Bruno gemmer teacher-cookien.
3. Sæt `studentCount` direkte i JSON-bodyen til et tal mellem 2 og 20 (standard 6), og send **Development → Seed FAQ demo**. Forvent `201 Created`. Ved en fejl vises den almindelige HTTP-status og response body; sessionsvariabler ændres kun ved succes.
4. Åbn den nye session i test-frontenden med samme underviser. Svaret indeholder session-id, studenterkode og underviserkode. En anden underviser kan joine med underviserkoden.
5. Send **Publish dummy generalization**, når du vil sende et fælles spørgsmål om de to første HelpRequests via Mimir. Forvent `202 Accepted`. Beskeden leveres asynkront gennem den normale outbox og RabbitMQ.
6. Brug **Get teacher queue** eller opdater køen i frontenden. En underviser kan tage og besvare FAQ'en. **Get first student request** og **Get second student request** viser de to demostuderendes samtaler.

Heimdall har to proxy-routes, som kun konfigureres i `Development`:

| Bruno-kald | Gateway-route | Service-route |
|---|---|---|
| Seed FAQ demo | `POST /api/teachers/development/seed` | Eir: `POST /api/test/seed` |
| Publish dummy generalization | `POST /api/teachers/development/generalizations` | Mimir: `POST /api/test/generalizations` |

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

Hver seed-kørsel opretter en ny session med nye studenterdeltagere og én HelpRequest pr. studerende. Der bruges både `Physical` og `Digital`. Eksisterende data bevares, og opstart seeder ikke data. FAQ'en sendes først, når du kører **Publish dummy generalization**.

Seed-servicen genbruger de eksisterende commands til sessioner, tilmelding og HelpRequests. Validering og beskedpublicering følger det normale flow. Hvis et senere command fejler, kan tidligere oprettede data fra kørslen være gemt; de kan slettes via den normale sessionsfunktion.

Ved `201` udfyldes de eksisterende `sessionId`, `sessionCode`, `teacherSessionCode`, `sessionGeneration` og `helpRequestId` i det valgte miljø. De øvrige EirService-kald kan derefter bruge testsessionen. Cookies, Mimir-payload og den interne `seedReady`-markering ligger som runtime-variabler i collectionen.

Student-kaldene vælger den relevante demo-cookie i Brunos normale cookiejar med en lille helper, ligesom teacher-login gør. Et efterfølgende almindeligt student-kald bruger derfor den sidst valgte demostuderende. Teacher-cookien bevares. Ryd studenter-cookien i Bruno, hvis du vil have en ny anonym studenteridentitet.

Demostuderende i Bruno har egne identiteter. En studerende i browseren kan joine med studenterkoden og oprette egne spørgsmål, hvis du vil teste accept/afvisning i UI'et. Brug da disse HelpRequest-id'er i Mimir-payloadet.

Payloadet til Mimir kan redigeres ved at erstatte `{{generalizationPayload}}` med JSON fra seed-svaret. Samme generaliserings-id kan sendes igen for at teste genlevering. Når Bruno lukkes, kan et nyt datasæt oprettes ved at køre seed-kaldet igen.
