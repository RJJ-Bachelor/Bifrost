# Eval-datasæt til generalisering

Datasættet måler, hvor godt Mimir generaliserer hjælpeanmodninger. Det tester to ting:

1. **Beslutningen:** om modellen vælger `standalone`, `join` eller `pair`, og om den vælger den rigtige kandidat.
2. **Generaliseringen:** om FAQ-spørgsmålet ved `pair` er formuleret rigtigt.

Reglerne står i rapportens afsnit 7.2.5 (GeneralizationService). Mappen indeholder kun data. Harnessen, der kører datasættet mod modellerne, laves i BF-10. Evals kører ikke i CI, fordi de kalder den rigtige model.

## Reglen kort

- Generalisér kun, når ét svar fra underviseren løser alle spørgsmålene. Det kræver typisk samme opgave og samme konkrete problem. Samme emne er ikke nok.
- Ved tvivl står spørgsmålet alene (`standalone`). En forkert sammenlægning er værre end en overset.
- Ved `pair` skrives et nyt FAQ-spørgsmål på dansk. Det må ikke indeholde navne eller personoplysninger og må ikke være mere specifikt end det, spørgsmålene har til fælles.
- Ved `join` fastholdes generaliseringens FAQ-spørgsmål. Der skrives ikke et nyt.

## Format

`generalization-cases.json` er et array af cases:

```json
{
  "id": "loop-never-stops-pair",
  "category": "clear-match",
  "note": "Samme opgave og samme problem: løkken stopper ikke. Kun den første nævner i++, så det må ikke stå i FAQ'en.",
  "sessionTitle": "Programmering 1 - løkker",
  "candidates": [
    { "id": "r1", "type": "request", "text": "opg 3 min while løkke stopper aldrig, jeg har prøvet at tilføje i++ men den kører stadig" }
  ],
  "newQuestion": "mit program i opgave 3 hænger bare og bliver ved med at skrive det samme tal ud",
  "expected": {
    "decision": "pair",
    "candidateId": "r1",
    "faq": {
      "example": "Hvorfor stopper løkken i opgave 3 aldrig?",
      "mustNotContain": ["i++"]
    }
  }
}
```

| Felt | Beskrivelse |
|---|---|
| `id` | Unikt navn på casen i kebab-case. |
| `category` | En af kategorierne nedenfor. |
| `note` | Hvorfor facit er, som det er. Skal altid udfyldes. |
| `sessionTitle` | Sessionens titel. Harnessen kan udelade den for at måle, om titlen hjælper. |
| `candidates` | Kandidaterne i køen. En `request` har `text`, en `generalization` har `faqQuestion`. Id'erne (`r1`, `g1`) gælder kun inden for casen. En tom liste betyder en tom kø. |
| `newQuestion` | Det nye spørgsmål, der skal vurderes. |
| `expected.decision` | `standalone`, `join` eller `pair`. |
| `expected.candidateId` | Den kandidat, der skal vælges. `null` ved `standalone`. |
| `expected.faq` | Kun ved `pair`. |
| `expected.faq.example` | Et eksempel på en god formulering. |
| `expected.faq.mustNotContain` | Tekst, der ikke må stå i FAQ-spørgsmålet: navne og detaljer, som kun ét af spørgsmålene nævner. |

En generalisering har kun sit FAQ-spørgsmål og ikke medlemmernes tekst, fordi det er det, modellen får i Mimir.

## Kategorier

| Kategori | Tester |
|---|---|
| `clear-match` | Samme opgave og samme problem, formuleret forskelligt. |
| `clear-different` | Forskellige opgaver eller problemer. |
| `borderline` | Tilfælde tæt på grænsen i begge retninger: samme emne med et andet problem, vage spørgsmål, næsten samme ordlyd om noget forskelligt og forskellig ordlyd om det samme problem. |
| `multi-candidate` | Realistiske køer med mange kandidater, hvor den rigtige skal vælges. |
| `typos` | Stavefejl, slang og forkortelser. |
| `names` | Navne og personoplysninger, der ikke må komme med i FAQ-spørgsmålet. |
| `injection` | Spørgsmål, der forsøger at styre modellen. De vurderes efter samme regel som alle andre. |
| `empty-queue` | Ingen kandidater. |

## Vurdering

- **Beslutning:** både `decision` og `candidateId` skal passe med facit.
- **FAQ-spørgsmål** (kun ved `pair`):
  - Automatisk: ingen af teksterne i `mustNotContain` må forekomme. Store og små bogstaver tæller ikke.
  - Manuelt eller med en sprogmodel som bedømmer, med `example` som reference: dækker det begge spørgsmål, er det ikke mere specifikt end det fælles, er det på dansk, og er det formuleret som et spørgsmål?
- **Ugyldige svar:** ugyldig JSON eller et id uden for kandidaterne tælles som ugyldigt og som `standalone`, ligesom Mimir behandler det.

Måltal:

- **Forkerte sammenlægninger:** `join` eller `pair`, hvor facit er `standalone`, eller hvor kandidaten er forkert. Det vigtigste tal.
- **Oversete:** `standalone`, hvor facit er `join` eller `pair`.
- **Korrekte beslutninger** i procent, samlet og pr. kategori.
- **Gyldig JSON** i procent.
- **FAQ-spørgsmål uden fund i `mustNotContain`** i procent.

## Nye cases

- Spørgsmålene skal handle om software.
- Skriv dem, som studerende skriver: små bogstaver, forkortelser og manglende tegnsætning.
- Udfyld altid `note`.
- Facit skal godkendes af gruppen.
