# Git- og Branch-strategi: Bifrost

Her beskrives vores Git-workflow. Formålet er at sikre et stabilt udviklingsmiljø, en overskuelig commit-historik uden lokal merge-spaghetti og optimal udnyttelse af vores automatiserede AI Pull Request (PR) review.

---

## 1. Kom godt i gang (Setup)
Når du har clonet repositoriet, køres følgende kommandoer **én gang** for at aktivere projektets delte aliases, rebase-indstillinger og Jira-hooks:

```bash
git config --local include.path ../.gitconfig
git config --local core.hooksPath .githooks
chmod +x .githooks/prepare-commit-msg 2>/dev/null || true
```

*(Bemærk: `--local` isolerer indstillingerne til dette repository. På Windows i Git Bash/PowerShell er `chmod` automatisk håndteret).*

---

## 2. Branch-struktur

* **`main` (Produktion):** Kun stabile releases. Ingen direkte commits.
* **`dev` (Integration):** Vores primære default branch, hvor alt arbejde integreres via PRs.
* **`feature/*` (Nye funktioner):** Oprettes ud fra `dev` (f.eks. `feature/BF-012-jwt-auth`).
* **`fix/*` (Fejlrettelser):** Oprettes ud fra `dev` til bugfixes (f.eks. `fix/BF-045-null-token`).

---

## 3. Commit-konventioner & Automatisk Jira-linking

Vi benytter **Conventional Commits** for at gøre historikken letlæselig for både teamet og vores AI-reviewer, som bruger præfikserne til at afkode hensigten med ændringerne:

* `feat:` Ny funktionalitet til brugeren/systemet.
* `fix:` Fejlrettelse i eksisterende kode.
* `refactor:` Kodeændringer, der hverken retter bugs eller tilføjer features (oprydning/arkitektur).
* `test:` Tilføjelse eller rettelse af tests.
* `docs:` Ændringer i dokumentation eller README.
* `chore:` Vedligeholdelse af build-scripts, dependencies eller config-filer.

> **Bemærk for feature-branches:** På en feature-branch (`feature/*`) antages commits som udgangspunkt at være ny feature-kode. Du behøver derfor ikke skrive `feat:` på hver enkelt commit, men brug `fix:` hvis et commit retter en fejl undervejs. Vores AI-reviewer bruger commit-historikken til at forstå udviklingsforløbet og intentionen bag kodeændringerne (og agerer ikke form-politi).

### Automatisk Jira-præfiks
Hvis dit branch-navn indeholder et Jira-nummer (f.eks. `feature/BF-012-tilføj-auth`), tilføjer vores Git-hook automatisk sagsnummeret foran din commit-besked:

* **Du skriver:** `git commit -m "feat: add token validation middleware"`
* **Git gemmer:** `[BF-012] feat: add token validation middleware`

Det gør det nemt at spore commits tilbage til Jira-issues.

---

## 4. Dagligt Workflow (Undgå lokal merge-spaghetti)

For at undgå unødvendige lokale "Merge branch 'dev' into feature/..." commits, **rebaser vi altid lokalt** frem for at merge:

```bash
git sync  # Henter dev og rebaser din branch (alias for: git fetch origin && git rebase origin/dev)
git pf    # Pusher sikkert efter rebase (alias for: git push --force-with-lease)
```

---


## 5. Arbejde på afhængige features (Stacked Branches)

Hvis du har en branch (`feature/A`), der venter på PR-review, og du skal bruge koden derfra i `feature/B`:

### Scenarie 1: feature/B er helt ny
Hvis du starter på en frisk feature, der bygger ovenpå A:
```bash
git checkout feature/A
git checkout -b feature/B
# Arbejd og commit som normalt
```

### Scenarie 2: feature/B eksisterer allerede
Hvis du allerede var i gang med B og pludselig mangler koden fra A:
```bash
git checkout feature/B
git rebase feature/A      # Flytter B, så den nu ligger oven på A
git pf                    # Pusher opdateringen til dit remote repository
```

---

### Fælles: Når feature/A er godkendt og merged ind i `dev`
Når PR'en for A er blevet merget ind i `dev`, skal B blot synkroniseres:

```bash
git checkout feature/B
git sync  # Henter nyeste dev og flytter dine feature/B-commits direkte over på dev
git pf    # Pusher opdateringen til GitHub
```
*(Git registrerer automatisk, at commits fra A nu ligger på `dev`, og efterlader udelukkende dine nye B-commits oven på `dev`).*

---

## 6. Integration via Pull Request

1. Opret en PR på GitHub med base `dev`.
2. Start AI-reviewet ved at skrive en kommentar på PR'en:
   ```text
   /review
   ```
3. Scriptet analyserer koden, opdaterer PR-beskrivelsen, foretager eventuelle automatiske opdateringer i `docs/PROJECT_CONTEXT.md` og indsender sit review (`APPROVE` eller `REQUEST_CHANGES`).
4. Ret eventuelle fund og genkør `/review` ved behov.
5. Når PR'en er godkendt og alle CI checks er grønne, foretages et standard **Merge Commit** ind i `dev`.

Når `feature/A` senere merges ind i `dev`, kører du bare `git sync` som normalt på `feature/B`. Git finder selv ud af, at A's commits nu er en del af `dev`, og rydder automatisk op.

---

## 7. Vedligeholdelse af `docs/PROJECT_CONTEXT.md`

`docs/PROJECT_CONTEXT.md` er AI-reviewerens arkitekturkontekst og opdateres automatisk af review-botten, når en PR introducerer reelle arkitekturændringer.

### Automatisk adfærd

* **Append-only som standard:** Botten tilføjer kun nye sektioner nederst i filen. Eksisterende indhold røres ikke, så små fejl i en model kan ikke ødelægge dokumentet.
* **Nederst gælder:** Ved konflikter mellem sektioner er det altid den nederste, nyeste sektion der er gældende. Det er noteret i prompten til AI-revieweren, så den læser filen korrekt.
* **Konsolidering ved 400 linjer:** Når filen overstiger 400 ikke-tomme linjer, skifter botten til konsolideringsmode og fletter de akkumulerede append-sektioner ind i en ren struktur. En guardrail blokerer commit'en, hvis indholdet krymper mere end 15 %.

### Manuel oprydning (anbefalet ind imellem)

Den automatiske konsolidering holder filen i skak, men menneskelig oprydning giver bedre resultater. Når filen begynder at føles rodet:

1. Åbn `docs/PROJECT_CONTEXT.md` lokalt.
2. Bed en AI (eller gør det selv) om at flette overlappende sektioner, fjerne forældede fakta og bevare alle gældende arkitekturkomponenter.
3. Erstat filen med den konsoliderede version.
4. Commit og push manuelt, fx:
   ```bash
   git commit -m "docs: manual consolidation of PROJECT_CONTEXT.md"
   ```
5. Næste auto-append lægger sig oven på den nye, rene version.

Manuel oprydning er ikke et krav — men det giver et bedre dokument end automatisk konsolidering alene, og det er den nemmeste måde at holde filen læsbar for både mennesker og AI.

---
