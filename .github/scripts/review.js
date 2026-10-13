const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const GITHUB_TOKEN = process.env.GITHUB_TOKEN;
const API_KEY = process.env.GEMINI_API_KEY_REVIEW;
const PR_NUMBER = process.env.PR_NUMBER;
const REPO = process.env.GITHUB_REPOSITORY;
const CONTEXT_FILE_PATH = path.join(process.cwd(), "docs", "PROJECT_CONTEXT.md");

const GITHUB_API = "https://api.github.com";
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// Model Pools
const LIGHT_MODELS = [
  "gemini-3.5-flash-lite",
  "gemini-3.1-flash-lite",
  "gemma-4-31b-it",
  "gemma-4-26b-a4b-it"
];
const HEAVY_MODELS = [
  "gemini-3.8-flash",
  "gemini-3.7-flash",
  "gemini-3.6-flash",
  "gemini-3.5-flash"
];

async function githubFetch(endpoint, options = {}) {
  const url = endpoint.startsWith("http") ? endpoint : `${GITHUB_API}${endpoint}`;
  const response = await fetch(url, {
    ...options,
    headers: {
      "Authorization": `Bearer ${GITHUB_TOKEN}`,
      "Accept": "application/vnd.github.v3+json",
      "User-Agent": "Bifrost-AI-Reviewer",
      ...options.headers
    }
  });

  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(`GitHub API request failed (${response.status} ${response.statusText}):${errorText}`);
  }

  const contentType = response.headers.get("content-type");
  if (contentType && contentType.includes("application/json")) {
    return await response.json();
  }
  return await response.text();
}

function extractAndCleanJson(rawText) {
  if (!rawText) return null;

  let cleaned = rawText
    .replace(/<\|channel\>thought[\s\S]*?<channel\|>/g, "")
    .replace(/```(?:json)?\s*([\s\S]*?)\s*```/gi, "$1")
    .replace(/`(\{[^`]*\})`/g, "$1")
    .trim();

  const jsonMatch = cleaned.match(/\{[\s\S]*\}/);
  if (jsonMatch) {
    try {
      return JSON.parse(jsonMatch[0]);
    } catch {
      // Ignore parse error
    }
  }
  return null;
}

async function requestGeminiModel(model, prompt, systemInstruction, expectJson = true, maxRetries = 2) {
  const generationConfig = {
    temperature: 0.2
  };

  if (expectJson) {
    generationConfig.responseMimeType = "application/json";
  }

  if (model.startsWith("gemini-3")) {
    generationConfig.thinkingConfig = { thinkingLevel: "MEDIUM" };
  }

  const url = "https://generativelanguage.googleapis.com/v1beta/models/" + encodeURIComponent(model) + ":generateContent?key=" + encodeURIComponent(API_KEY);

  let attempt = 0;
  while (true) {
    attempt++;
    const response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        contents: [{ role: "user", parts: [{ text: prompt }] }],
        systemInstruction: { parts: [{ text: systemInstruction }] },
        generationConfig
      })
    });

    if (!response.ok) {
      const errText = await response.text();
      // Retry on transient server/load errors (503 High Demand / Unavailable, 429 Rate Limit, 500+)
      if ((response.status === 503 || response.status === 429 || response.status >= 500) && attempt <= maxRetries) {
        const delayMs = attempt * 3000;
        console.warn(`⏳ Model ${model} returned status ${response.status} (attempt ${attempt}/${maxRetries + 1}). Retrying in ${delayMs / 1000}s...`);
        await sleep(delayMs);
        continue;
      }
      throw new Error(`Model ${model} returned status ${response.status}: ${errText}`);
    }

    const data = await response.json();
    const parts = data.candidates?.[0]?.content?.parts || [];
    const answerPart = parts.find((p) => !p.thought) || parts[parts.length - 1];
    const rawText = answerPart?.text;

    if (!expectJson) {
      return rawText ? rawText.trim() : null;
    }

    const parsedJson = extractAndCleanJson(rawText);
    if (!parsedJson && rawText) {
      console.warn(`Model ${model} returned non-parsable JSON output:\n${rawText}`);
    }
    return parsedJson;
  }
}

function loadRequestedFiles(requestedFiles, loadedFilesMap) {
  if (!Array.isArray(requestedFiles) || requestedFiles.length === 0) {
    return 0;
  }

  const MAX_FILE_SIZE = 100 * 1024; // 100 KB limit per file
  const IGNORED_EXTENSIONS = [
    ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
    ".lock", ".pdf", ".zip", ".tar.gz", ".dll", ".exe",
    ".ttf", ".woff", ".woff2"
  ];
  let newFilesCount = 0;

  for (const rawPath of requestedFiles) {
    if (typeof rawPath !== "string") continue;
    const cleanPath = rawPath.replace(/[`'"]/g, "").replace(/^[ab]\//, "").trim();
    if (!cleanPath || cleanPath === "docs/PROJECT_CONTEXT.md") continue;
    if (loadedFilesMap.has(cleanPath)) continue;
    if (IGNORED_EXTENSIONS.some((ext) => cleanPath.toLowerCase().endsWith(ext))) continue;

    // Prevent path traversal outside the repository
    const fullPath = path.resolve(process.cwd(), cleanPath);
    if (!fullPath.startsWith(process.cwd() + path.sep)) {
      console.warn(`Path traversal attempt blocked: ${cleanPath}`);
      continue;
    }

    if (!fs.existsSync(fullPath)) {
      console.warn(`Requested file not found on disk: ${cleanPath}`);
      continue;
    }

    try {
      const stat = fs.statSync(fullPath);
      if (stat.isDirectory() || stat.size > MAX_FILE_SIZE) continue;

      const content = fs.readFileSync(fullPath, "utf-8");
      loadedFilesMap.set(cleanPath, content);
      newFilesCount++;
    } catch (err) {
      console.warn(`Could not read requested file ${cleanPath}: ${err.message}`);
    }
  }

  return newFilesCount;
}

function formatLoadedFiles(loadedFilesMap) {
  if (!loadedFilesMap || loadedFilesMap.size === 0) {
    return "";
  }
  const parts = [];
  for (const [filePath, content] of loadedFilesMap.entries()) {
    parts.push(`#### File: \`${filePath}\`\n\`\`\`\n${content}\n\`\`\``);
  }
  return parts.join("\n\n");
}

function getRepositoryFileList() {
  try {
    const output = execSync("git ls-files", { encoding: "utf-8" });
    const IGNORED_EXTENSIONS = [
      ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
      ".pdf", ".zip", ".tar.gz", ".lock", ".dll", ".exe",
      ".ttf", ".woff", ".woff2"
    ];

    return output
      .split("\n")
      .map((line) => line.trim())
      .filter((line) => {
        if (!line) return false;
        if (line.startsWith(".github/") || line.startsWith(".githooks/") || line.startsWith(".")) return false;
        return !IGNORED_EXTENSIONS.some((ext) => line.toLowerCase().endsWith(ext));
      })
      .join("\n");
  } catch {
    return "";
  }
}

function buildSystemInstruction(mode, hasFileContext = false) {
  let contextRule = "";
  let verdictEnum = "";

  if (mode === "TRIAGE") {
    contextRule = `6. **Obligatorisk kompleksitets-score og eskalering:**
Du SKAL starte dit JSON-svar med at evaluere PR'ens kompleksitet ("complexity_evaluation") ud fra denne faste rubrik:
* **Score 1-3 (Lav / Trivial):** Stavefejl, dokumentation, simple bugfixes i 1 fil (< 30 linjer), små CSS/tekst-ændringer.
  -> Du håndterer og godkender/kommenterer selv koden ("APPROVE" / "REQUEST_CHANGES" / "COMMENT").
* **Score 4-5 (Moderat):** Enkelt nyt endpoint eller metode inden for et kendt mønster.
  -> Du håndterer den selv, medmindre du mangler en konkret fil (sæt da "verdict": "NEED_CONTEXT").
* **Score 6-8 (Høj kompleksitet):** Ændringer på tværs af lag (Domain, Application, Api), nye interfaces/services, database/EF Core ændringer, async/concurrency.
  -> Du SKAL eskalere til en heavy model! Sæt "verdict": "ESCALATE" (eller "NEED_CONTEXT" hvis du mangler specifikke filer fra 'Repository Files').
* **Score 9-10 (Kritisk):** Dybe refactorings, nye kernemoduler, ændring af auth/sikkerhed, globale middlewares.
  -> Du SKAL sætte "verdict": "ESCALATE".

Hvis "scope_score" >= 6 eller "cross_layer_impact" er true, må du ALDRIG godkende selv. Du SKAL eskalere.`;
    verdictEnum = `"APPROVE" | "REQUEST_CHANGES" | "COMMENT" | "ESCALATE" | "NEED_CONTEXT"`;
  } else if (mode === "HEAVY_INVESTIGATE" || mode === "INVESTIGATE") {
    contextRule = `6. **Supplerende fil-efterspørgsel:**
* Som senior tech lead ræsonnerer du i dybden. Hvis du har brug for at inspicere en afhængighed, et interface, en model eller en relateret service for at verificere ændringerne med fuld sikkerhed, kan du sætte "verdict": "NEED_CONTEXT", liste de præcise filstier fra 'Repository Files' i "requested_files" og forklare hvorfor i "context_reason".
* Hvis du allerede har tilstrækkelig information i diff'et og den hidtidige kontekst, SKAL du levere det endelige review med "APPROVE", "REQUEST_CHANGES" eller "COMMENT".`;
    verdictEnum = `"APPROVE" | "REQUEST_CHANGES" | "COMMENT" | "NEED_CONTEXT"`;
  } else {
    contextRule = `6. **Endelig anmeldelse:** Du SKAL levere en fuld og endelig anmeldelse med verdict "APPROVE", "REQUEST_CHANGES" eller "COMMENT". Du må IKKE eskalere eller bede om yderligere filer.`;
    verdictEnum = `"APPROVE" | "REQUEST_CHANGES" | "COMMENT"`;
  }

  const fileContextSection = hasFileContext
    ? `\n* **Supplerende fil-kontekst:** Du har fået det fulde indhold af udvalgte filer under 'Requested File Contents'. Brug denne kontekst til at forstå arkitektur og afhængigheder, men fokuser dine fund og kommentarer på de konkrete ændringer i 'Git Diff'.`
    : "";

  const triageComplexityField = mode === "TRIAGE"
    ? `  "complexity_evaluation": {
    "scope_score": 1-10,
    "cross_layer_impact": true | false,
    "has_unseen_dependencies": true | false,
    "reasoning": "Kort begrundelse for scoren (1 linje)"
  },
`
    : "";

  return `
Du er en erfaren softwarearkitekt og tech lead, der anmelder et bachelorprojekt i softwareteknologi (Bifrost).

## Sprog & Tone
* Selve anmeldelsen og forklaringerne skrives på **dansk**, men alle tekniske begreber holdes på **engelsk** (f.eks. "Dependency Injection", "PR", "Controller", "Domain Model", "Repository", "DTO", "Race Condition").
* Benyt sandwich-modellen:
  1. Start med ros for gode løsninger (kun hvis der rent faktisk er noget at rose). HOLD DET HELT KORT (max to linjer!).
  2. Gennemgå konkrete fejl, mangler eller arkitekturbrud.
  3. Afslut med en opmuntrende og konstruktiv bemærkning (igen helt kort (ikke mere end en linje)).
* **INGEN STØJ:** Find ALDRIG på ligegyldige nitpicks. Hvis koden er god, så godkend den kortfattet.${fileContextSection}

## Fokusområder
1. **Engelsk i kodebasen:** Verificer at alle kodekommentarer, logbeskeder, fejltekster, variabel-/klassenavne og dokumentation i koden er skrevet 100% på **engelsk**.
2. **Logiske fejl & Bugs:** Off-by-one errors, manglende fejlhåndtering, race conditions, async/await-fejl, ubeskyttede nulls.
3. **Clean Architecture & Mappestruktur:** Tjek at afhængigheder peger indad. Ingen databasekald i controllers eller forretningslogik i forkerte lag.
4. **Tests (Non-blocking):** Gør venligt opmærksom på manglende tests ved ændret kerneforretningslogik.
5. **Opfølgning på historik:** Tjek om tidligere påpegede fejl er blevet udbedret.
${contextRule}

## Forståelse af Commit-historik & Intention
* Under 'Commit History' har du den kronologiske liste over commits på PR-branchen.
* Kæd commits sammen med ændringerne i 'Git Diff' for at forstå udviklingsforløbet og intentionen bag koden:
  - På feature-branches (f.eks. 'feature/*') antages alle commits implicit at være ny feature-funktionalitet ('feat'), medmindre andet fremgår. Udviklerne behøver derfor IKKE skrive 'feat:' på hver commit.
  - Commits der indeholder 'fix' (f.eks. 'fix:', 'bugfix' eller fejlrettelser) indikerer målrettede rettelser af problemer eller bugs opstået undervejs i udviklingen.
  - Commits der nævner refactor, rename eller cleanup forklarer strukturelle ændringer i diff'et.
* **INGEN COMMIT-POLITI:** Du må ALDRIG kritisere eller afvise en PR pga. commit-beskedernes formatering eller manglende konventionelle præfikser. Historikken er udelukkende en hjælp til dig til at forstå udviklernes intention.

## Output Format (JSON)
Du SKAL svare i dette JSON-skema:
{
${triageComplexityField}  "pr_summary_description": "Kort struktureret beskrivelse af PR'ens formål og ændringer (på dansk)",
  "verdict": ${verdictEnum},
  "requested_files": ["sti/til/fil.cs"],
  "context_reason": "Kort forklaring på hvorfor du mangler kontekst (kun relevant ved NEED_CONTEXT)",
  "summary": "Den samlede anmeldelse med sandwich-modellen (Markdown)",
  "inline_comments": [
    {
      "path": "sti/til/fil.ts",
      "snippet": "den specifikke linje kode der har en fejl",
      "comment": "Konstruktiv forklaring og forslag til rettelse (Markdown)"
    }
  ]
}
`;
}

function extractFilePathFromDiffHeader(headerLine) {
  // Case 1: Quoted paths, e.g. diff --git "a/foo bar.cs" "b/foo bar.cs"
  const quotedMatch = headerLine.match(/^diff --git\s+"[ab]\/(.+?)"\s+"[ab]\/(.+?)"/);
  if (quotedMatch) {
    const b = quotedMatch[2].trim();
    return b !== "dev/null" ? b : quotedMatch[1].trim();
  }

  // Case 2: Unquoted paths, e.g. diff --git a/foo/bar.cs b/foo/bar.cs
  const unquotedMatch = headerLine.match(/^diff --git\s+[ab]\/(.+?)\s+[ab]\/(.+)/);
  if (unquotedMatch) {
    const b = unquotedMatch[2].trim();
    return b !== "dev/null" ? b : unquotedMatch[1].trim();
  }

  return "";
}

const FAST_PATH_MIN_LINES = 160;
const FAST_PATH_MIN_CHARS = 8000;

function shouldFastPath(diffText) {
  if (!diffText) return false;

  const chunks = diffText.split(/(?=^diff --git )/m);
  let substantiveAddedLines = 0;
  let substantiveChars = 0;

  for (const chunk of chunks) {
    if (!chunk.trim()) continue;

    const firstLine = chunk.split("\n")[0] || "";
    const filePath = extractFilePathFromDiffHeader(firstLine);

    // Skip EF Core migration schema files from triggering fast-path
    if (filePath && (filePath.includes("/Migrations/") || filePath.includes("\\Migrations\\"))) {
      continue;
    }

    const lines = chunk.split("\n");
    for (const line of lines) {
      // Only count added lines (+), strictly ignore deletions (-) and diff headers (+++)
      if (line.startsWith("+") && !line.startsWith("+++")) {
        const content = line.substring(1).trim();
        // Skip empty lines, standalone brackets, and single-line / comment lines
        if (
          !content ||
          content === "{" ||
          content === "}" ||
          content === "};" ||
          content.startsWith("//") ||
          content.startsWith("/*") ||
          content.startsWith("*") ||
          content.startsWith("#")
        ) {
          continue;
        }
        substantiveAddedLines++;
        substantiveChars += content.length;
      }
    }
  }

  if (substantiveAddedLines > FAST_PATH_MIN_LINES || substantiveChars > FAST_PATH_MIN_CHARS) {
    console.log(`⚡ Substantial PR detected (${substantiveAddedLines} substantive added lines, ${substantiveChars} chars in core files). Fast-pathing directly to heavy reasoning models.`);
    return true;
  }

  return false;
}

async function performReview(basePrompt, diffText = "") {
  const loadedFiles = new Map();
  let escalated = shouldFastPath(diffText);

  // Phase 1: Fast triage with light models (LIGHT_MODELS)
  if (!escalated) {
    console.log("Evaluating PR with light triage models...");
    for (const model of LIGHT_MODELS) {
      try {
        console.log(`Attempting triage with light model: ${model}...`);
        const fastInstruction = buildSystemInstruction("TRIAGE", false);
        const result = await requestGeminiModel(model, basePrompt, fastInstruction, true);

        if (result) {
          if (result.complexity_evaluation) {
            const comp = result.complexity_evaluation;
            const score = Number(comp.scope_score) || 0;
            const isCrossLayer = comp.cross_layer_impact === true || String(comp.cross_layer_impact).toLowerCase() === "true";
            const hasUnseen = comp.has_unseen_dependencies === true || String(comp.has_unseen_dependencies).toLowerCase() === "true";

            console.log(`📊 PR Complexity [${model}]: ${score}/10 (Cross-layer: ${isCrossLayer}, Unseen deps: ${hasUnseen})`);
            if (comp.reasoning) {
              console.log(`   Reasoning: ${comp.reasoning}`);
            }

            // Programmatic safety guardrail: If model rated complexity >= 6 or cross-layer impact, enforce escalation
            if ((score >= 6 || isCrossLayer) && result.verdict !== "NEED_CONTEXT" && result.verdict !== "ESCALATE") {
              console.log(`⚡ Complexity evaluation (score: ${score}/10, cross-layer: ${isCrossLayer}) requires heavy reasoning model. Overriding verdict to ESCALATE.`);
              result.verdict = "ESCALATE";
            }
          }

          if (result.verdict === "NEED_CONTEXT") {
            console.log(`⚡ ${model} requested additional file context: ${JSON.stringify(result.requested_files || [])}`);
            if (result.context_reason) {
              console.log(`   Reason: ${result.context_reason}`);
            }
            loadRequestedFiles(result.requested_files, loadedFiles);
            console.log("⚡ Escalating review to heavy model pool with requested file context...");
            escalated = true;
            break;
          } else if (result.verdict === "ESCALATE") {
            console.log(`⚡ ${model} requested escalation due to high PR complexity. Escalating to heavy reasoning models...`);
            escalated = true;
            break;
          } else {
            console.log(`✅ Review successfully completed by light model: ${model}`);
            return result;
          }
        }
      } catch (err) {
        console.warn(`Light model ${model} failed (${err.message}). Trying next light model...`);
        await sleep(1500);
      }
    }
  }

  if (!escalated) {
    console.warn("All light triage models failed or were unavailable. Falling back directly to heavy models...");
  }

  // Phase 2: Try Heavy Models (with controlled 2-round deep context investigation)
  console.log("Attempting review with heavy reasoning models...");
  for (const model of HEAVY_MODELS) {
    try {
      console.log(`Attempting deep review with heavy model: ${model}...`);

      // Round 1: Heavy model receives current context and can either complete review or request deeper context
      let filesText = formatLoadedFiles(loadedFiles);
      let heavyPrompt = filesText
        ? `${basePrompt}\n\n### Requested File Contents (Full Context from PR branch):\n${filesText}\n`
        : basePrompt;

      const investigateInstruction = buildSystemInstruction("HEAVY_INVESTIGATE", loadedFiles.size > 0);
      let result = await requestGeminiModel(model, heavyPrompt, investigateInstruction, true);

      if (result && result.verdict === "NEED_CONTEXT") {
        console.log(`⚡ ${model} requested deeper file context: ${JSON.stringify(result.requested_files || [])}`);
        if (result.context_reason) {
          console.log(`   Reason: ${result.context_reason}`);
        }
        const addedCount = loadRequestedFiles(result.requested_files, loadedFiles);
        if (addedCount > 0) {
          console.log(`Loaded ${addedCount} additional file(s) into context. Executing final evaluation round with ${model}...`);
          filesText = formatLoadedFiles(loadedFiles);
          heavyPrompt = `${basePrompt}\n\n### Requested File Contents (Full Context from PR branch):\n${filesText}\n`;
        }

        // Round 2: Forced final review (cannot request additional context)
        const finalInstruction = buildSystemInstruction("FINAL", loadedFiles.size > 0);
        result = await requestGeminiModel(model, heavyPrompt, finalInstruction, true);
      }

      if (result && result.verdict !== "NEED_CONTEXT" && result.verdict !== "ESCALATE") {
        console.log(`✅ Review successfully completed by heavy model: ${model}`);
        return result;
      }
    } catch (err) {
      console.warn(`Heavy model ${model} unavailable (${err.message}). Trying next heavy model...`);
      await sleep(1500);
    }
  }

  // Phase 3: Emergency fallback queue with light models (can still request context if needed)
  console.log("All heavy models failed. Falling back to light models with file context investigation as emergency backup...");
  for (const model of LIGHT_MODELS) {
    try {
      console.log(`Attempting emergency review with light model: ${model}...`);
      let filesText = formatLoadedFiles(loadedFiles);
      let emergencyPrompt = filesText
        ? `${basePrompt}\n\n### Requested File Contents (Full Context from PR branch):\n${filesText}\n`
        : basePrompt;

      // Round 1: Light model can request context or finish review
      const fallbackInstruction = buildSystemInstruction("INVESTIGATE", loadedFiles.size > 0);
      let result = await requestGeminiModel(model, emergencyPrompt, fallbackInstruction, true);

      if (result && result.verdict === "NEED_CONTEXT") {
        console.log(`⚡ Emergency light model ${model} requested deeper file context: ${JSON.stringify(result.requested_files || [])}`);
        if (result.context_reason) {
          console.log(`   Reason: ${result.context_reason}`);
        }
        const addedCount = loadRequestedFiles(result.requested_files, loadedFiles);
        if (addedCount > 0) {
          console.log(`Loaded ${addedCount} additional file(s) into context. Executing final evaluation round with ${model}...`);
          filesText = formatLoadedFiles(loadedFiles);
          emergencyPrompt = `${basePrompt}\n\n### Requested File Contents (Full Context from PR branch):\n${filesText}\n`;
        }

        // Round 2: Forced final review
        const finalInstruction = buildSystemInstruction("FINAL", loadedFiles.size > 0);
        result = await requestGeminiModel(model, emergencyPrompt, finalInstruction, true);
      }

      if (result && result.verdict !== "NEED_CONTEXT" && result.verdict !== "ESCALATE") {
        console.log(`✅ Review successfully completed by emergency light model: ${model}`);
        return result;
      }
    } catch (err) {
      console.warn(`Emergency fallback model ${model} failed (${err.message}). Trying next...`);
      await sleep(1500);
    }
  }

  throw new Error("All review models in all tiers failed.");
}

const IGNORED_DIFF_PATTERNS = [
  /(?:^|[\\/])docs[\\/]PROJECT_CONTEXT\.md$/i,
  /package-lock\.json$/i,
  /packages\.lock\.json$/i,
  /yarn\.lock$/i,
  /pnpm-lock\.yaml$/i,
  /\.Designer\.cs$/i,
  /ModelSnapshot\.cs$/i,
  /\.min\.(js|css)$/i,
  /\.map$/i
];

function filterDiff(diffText) {
  return diffText
    .split(/(?=^diff --git )/m)
    .filter((chunk) => {
      const firstLine = chunk.split("\n")[0] || "";
      const filePath = extractFilePathFromDiffHeader(firstLine);
      if (!filePath) return true;
      return !IGNORED_DIFF_PATTERNS.some((pattern) => pattern.test(filePath));
    })
    .join("");
}

function parseDiffLines(diffText) {
  const fileLinesMap = new Map();
  const fileDiffs = diffText.split(/(?=^diff --git )/m);

  for (const fileDiff of fileDiffs) {
    if (!fileDiff.trim()) continue;
    const firstLine = fileDiff.split("\n")[0] || "";
    const filePath = extractFilePathFromDiffHeader(firstLine);
    if (!filePath) continue;

    const addedLines = [];
    let currentNewLine = 0;
    let inHunk = false;
    const lines = fileDiff.split("\n");

    for (const line of lines) {
      const hunkHeader = line.match(/^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/);
      if (hunkHeader) {
        currentNewLine = parseInt(hunkHeader[1], 10);
        inHunk = true;
        continue;
      }

      if (!inHunk) continue;

      if (line.startsWith("+") && !line.startsWith("+++")) {
        addedLines.push({
          line: currentNewLine,
          content: line.substring(1).trim()
        });
        currentNewLine++;
      } else if (!line.startsWith("-")) {
        currentNewLine++;
      }
    }

    fileLinesMap.set(filePath, addedLines);
  }
  return fileLinesMap;
}

function matchSnippetToLine(fileLinesMap, rawPath, snippet) {
  if (!rawPath || !snippet) return null;

  // Clean up relative prefixes like ./, leading /, or a/ / b/ git prefixes
  const cleanPath = rawPath.replace(/[`'"]/g, "").replace(/^\.?\//, "").replace(/^[ab]\//, "").trim();
  const addedLines = fileLinesMap.get(cleanPath);
  if (!addedLines) return null;

  const normalizedSnippet = snippet.trim();
  if (!normalizedSnippet) return null;

  const exactMatch = addedLines.find((item) => item.content === normalizedSnippet);
  if (exactMatch) return exactMatch.line;

  // Prevent matching empty lines or matching against empty snippets
  const partialMatch = addedLines.find(
    (item) => item.content.length > 0 &&
      (item.content.includes(normalizedSnippet) || normalizedSnippet.includes(item.content))
  );
  return partialMatch ? partialMatch.line : null;
}

async function updateContextFile(diff, prBranch) {
  let currentContext = "";
  if (fs.existsSync(CONTEXT_FILE_PATH)) {
    currentContext = fs.readFileSync(CONTEXT_FILE_PATH, "utf-8");
  }

  const CONTEXT_LINE_THRESHOLD = 400;
  const nonEmptyLineCount = currentContext.split("\n").filter((l) => l.trim().length > 0).length;
  const needsConsolidation = nonEmptyLineCount > CONTEXT_LINE_THRESHOLD;

  if (needsConsolidation) {
    console.log(`📚 PROJECT_CONTEXT.md is large (${nonEmptyLineCount} non-empty lines > ${CONTEXT_LINE_THRESHOLD}). Running consolidation mode.`);
  }

  const systemInstruction = needsConsolidation
    ? `
You are a software architecture documentation assistant for the Bifrost project.
The existing 'docs/PROJECT_CONTEXT.md' has grown large and is now in CONSOLIDATION mode.

Rules:
1. If the PR ONLY contains bugfixes, refactoring, documentation, tests, minor UI tweaks, or code that fits within existing architecture, return EXACTLY: [NO_CHANGES]
2. Otherwise, output the COMPLETE consolidated Markdown file.
3. You MAY merge duplicate or overlapping sections, remove outdated entries, and reorder for clarity. The goal is a clean, accurate, high-level architecture document.
4. You MUST NOT lose factual information: every architecture concept, module, endpoint, dependency rule, and integration that is still true must remain present — either in its original form or in a clearly equivalent consolidated form.
5. Write 100% in English without markdown code blocks (\`\`\`markdown ... \`\`\`).
6. Maintain high-level architectural value. Do NOT log commit histories or small implementation details.
`
    : `
You are a software architecture documentation assistant for the Bifrost project.
The existing 'docs/PROJECT_CONTEXT.md' is APPEND-ONLY. You must NEVER rewrite, reorder, or remove existing content — only append a new section at the very end.

Rules:
1. If the PR ONLY contains bugfixes, refactoring, documentation, tests, minor UI tweaks, or code that fits within existing architecture, return EXACTLY: [NO_CHANGES]
2. Otherwise, output ONLY the new Markdown section to be appended. Do NOT include any of the existing content. Do NOT wrap the output in code blocks.
3. The new section MUST begin with a level-2 heading in the form: ## Update YYYY-MM-DD (PR #NNN)
4. List ONLY the new or changed architectural facts. Keep it concise — typically 5 to 20 bullet points.
5. Write 100% in English.
6. Later sections override earlier sections when facts conflict. This is expected and intentional — do NOT try to resolve conflicts.
`;

  const prompt = `
Existing PROJECT_CONTEXT.md:
${currentContext}

PR Git Diff:
\`\`\`diff
${diff}
\`\`\`
`;

  try {
    console.log("Evaluating whether docs/PROJECT_CONTEXT.md requires updates...");
    let response = null;
    let usedModel = null;

    for (const model of [...LIGHT_MODELS, ...HEAVY_MODELS]) {
      try {
        response = await requestGeminiModel(model, prompt, systemInstruction, false);
        if (response) {
          usedModel = model;
          break;
        }
      } catch (err) {
        console.warn(`Context evaluation with ${model} failed (${err.message}). Trying next...`);
        await sleep(1500);
      }
    }

    if (!response) {
      console.warn("Could not evaluate PROJECT_CONTEXT.md updates with available models.");
      return;
    }

    if (response.includes("[NO_CHANGES]")) {
      console.log(`[${usedModel}] No architectural changes detected. Skipping PROJECT_CONTEXT.md update.`);
      return;
    }

    const cleanedResponse = response
      .replace(/^```markdown\s*/i, "")
      .replace(/^```\s*/i, "")
      .replace(/```\s*$/i, "")
      .trim();

    if (!cleanedResponse) {
      console.warn("Model returned empty context update. Skipping.");
      return;
    }

    let newFileContent;
    let commitMessage;
    let logLabel;

    if (needsConsolidation) {
      const oldNonEmpty = currentContext.split("\n").filter((l) => l.trim().length > 0).length;
      const newNonEmpty = cleanedResponse.split("\n").filter((l) => l.trim().length > 0).length;
      const ratio = oldNonEmpty > 0 ? newNonEmpty / oldNonEmpty : 1;

      if (ratio < 0.85) {
        console.warn(`⚡ Consolidation shrank content by ${Math.round((1 - ratio) * 100)}% (${oldNonEmpty} -> ${newNonEmpty} non-empty lines). Skipping commit to avoid data loss.`);
        return;
      }
      if (cleanedResponse === currentContext.trim()) {
        console.log(`[${usedModel}] Consolidation produced no change. Skipping.`);
        return;
      }

      newFileContent = cleanedResponse + "\n";
      commitMessage = "docs: auto-consolidate PROJECT_CONTEXT.md [skip review]";
      logLabel = "consolidated";
    } else {
      if (cleanedResponse === currentContext.trim()) {
        console.log(`[${usedModel}] Append produced no change. Skipping.`);
        return;
      }

      const separator = currentContext.trim().length > 0 ? "\n\n---\n\n" : "";
      newFileContent = currentContext.trimEnd() + separator + cleanedResponse + "\n";
      commitMessage = "docs: auto-append to PROJECT_CONTEXT.md [skip review]";
      logLabel = "appended";
    }

    if (newFileContent && newFileContent !== currentContext) {
      execSync(`git fetch origin ${prBranch}`);
      execSync(`git checkout -B ${prBranch} FETCH_HEAD`);

      const latestContext = fs.existsSync(CONTEXT_FILE_PATH)
        ? fs.readFileSync(CONTEXT_FILE_PATH, "utf-8")
        : "";

      if (latestContext && latestContext !== currentContext) {
        console.warn("PROJECT_CONTEXT.md changed on disk since evaluation. Aborting to avoid clobbering concurrent updates.");
        return;
      }

      fs.mkdirSync(path.dirname(CONTEXT_FILE_PATH), { recursive: true });
      fs.writeFileSync(CONTEXT_FILE_PATH, newFileContent, "utf-8");

      execSync("git config user.name 'github-actions[bot]'");
      execSync("git config user.email 'github-actions[bot]@users.noreply.github.com'");
      execSync("git add docs/PROJECT_CONTEXT.md");
      execSync(`git commit -m '${commitMessage}'`);
      execSync(`git push origin ${prBranch}`);
      console.log(`[${usedModel}] docs/PROJECT_CONTEXT.md ${logLabel} and committed to ${prBranch}.`);
      return execSync("git rev-parse HEAD", { encoding: "utf-8" }).trim();
    }
  } catch (err) {
    console.warn("Could not automatically update PROJECT_CONTEXT.md:", err.message);
  }
  return null;
}

async function run() {
  const pr = await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}`);

  // Ensure local repository is checked out to the PR branch commit so on-demand file reads and local diff match PR state
  let checkedOut = false;
  try {
    console.log(`Checking out PR branch ${pr.head.ref} (${pr.head.sha})...`);
    try {
      execSync(`git fetch origin ${pr.base.ref}`, { stdio: "ignore" });
    } catch {
      // Ignore if base branch is already up-to-date
    }
    try {
      execSync(`git fetch origin ${pr.head.ref}`, { stdio: "ignore" });
    } catch {
      // Ignore if head branch is already up-to-date
    }
    execSync(`git checkout ${pr.head.sha}`, { stdio: "ignore" });
    checkedOut = true;
  } catch (err) {
    console.warn(`Could not checkout PR branch locally (${err.message}). Reading disk files will use current branch.`);
  }

  let diff = "";
  try {
    diff = await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}`, {
      headers: { "Accept": "application/vnd.github.v3.diff" }
    });
  } catch (err) {
    if (err.message.includes("406") || err.message.includes("maximum number of files")) {
      console.warn("⚡ GitHub API diff limit exceeded (406). Falling back to local git diff...");
      try {
        diff = execSync(`git diff -M30% -l0 origin/${pr.base.ref}...${pr.head.sha}`, {
          encoding: "utf-8",
          maxBuffer: 30 * 1024 * 1024
        });
      } catch (gitErr) {
        if (gitErr.message.includes("maxBuffer")) {
          throw new Error("Local git diff exceeded maxBuffer (30 MB). Cannot compute PR diff.");
        }
        if (!checkedOut) {
          throw new Error("Local git diff fallback unavailable: PR branch was not checked out successfully.");
        }
        console.warn(`Triple-dot git diff failed (${gitErr.message}). Trying fallback git diff against HEAD...`);
        try {
          diff = execSync(`git diff -M30% -l0 origin/${pr.base.ref}...HEAD`, {
            encoding: "utf-8",
            maxBuffer: 30 * 1024 * 1024
          });
        } catch (headErr) {
          if (headErr.message.includes("maxBuffer")) {
            throw new Error("Local git diff exceeded maxBuffer (30 MB). Cannot compute PR diff.");
          }
          throw headErr;
        }
      }
    } else {
      throw err;
    }
  }

  const previousComments = await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}/comments`);
  const historicalFeedback = Array.isArray(previousComments)
    ? previousComments.map((c) => `[File: ${c.path} Line: ${c.line}]: ${c.body}`).join("\n")
    : "";

  let commitHistoryText = "";
  try {
    const commitsData = await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}/commits`);
    if (Array.isArray(commitsData) && commitsData.length > 0) {
      commitHistoryText = commitsData
        .map((c) => {
          const sha = c.sha ? c.sha.substring(0, 7) : "";
          const msg = c.commit?.message ? c.commit.message.split("\n")[0].trim() : "";
          return `* ${sha}: ${msg}`;
        })
        .filter(Boolean)
        .join("\n");
    }
  } catch (err) {
    console.warn(`Could not fetch PR commits (${err.message}). Continuing without commit history.`);
  }

  let projectContext = "";
  if (fs.existsSync(CONTEXT_FILE_PATH)) {
    projectContext = fs.readFileSync(CONTEXT_FILE_PATH, "utf-8");
  }

  const reviewDiff = filterDiff(diff);
  const repoFiles = getRepositoryFileList();

  const prompt = `
PR Title: ${pr.title}
PR Branch: ${pr.head.ref}
PR Author: ${pr.user.login}

Commit History (Chronological):
${commitHistoryText || "No commit history available."}

Project Context (docs/PROJECT_CONTEXT.md):
${projectContext || "No additional project context provided."}

Note on Project Context format: The document is append-only. Later sections (nearer the bottom) override earlier sections when facts conflict. When reading it, treat the bottom-most statement about a given topic as the current truth.

Historical Review Feedback:
${historicalFeedback || "No previous review comments."}

Repository Files (Exact relative paths available for requested_files):
\`\`\`text
${repoFiles || "No files available."}
\`\`\`

Git Diff:
\`\`\`diff
${reviewDiff}
\`\`\`
`;

  const aiResult = await performReview(prompt, reviewDiff);

  if (!pr.body || pr.body.trim().length === 0) {
    if (aiResult.pr_summary_description) {
      await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          body: `### 📋 PR Beskrivelse (Autogenereret)\n\n${aiResult.pr_summary_description}`
        })
      });
    }
  }

  const fileLinesMap = parseDiffLines(reviewDiff);
  const validComments = [];
  const unmatchedComments = [];

  if (Array.isArray(aiResult.inline_comments)) {
    for (const item of aiResult.inline_comments) {
      const cleanPath = typeof item.path === "string"
        ? item.path.replace(/[`'"]/g, "").replace(/^\.?\//, "").replace(/^[ab]\//, "").trim()
        : "";
      const line = matchSnippetToLine(fileLinesMap, cleanPath, item.snippet);
      if (line) {
        validComments.push({
          path: cleanPath,
          line: line,
          side: "RIGHT",
          body: item.comment
        });
      } else {
        unmatchedComments.push(`* **${cleanPath || item.path}:** ${item.comment}`);
      }
    }
  }

  let finalSummary = aiResult.summary || "Review completed.";
  if (unmatchedComments.length > 0) {
    finalSummary += `\n\n### 💬 Yderligere bemærkninger\n${unmatchedComments.join("\n")}`;
  }

  // Update documentation before submitting review to prevent stale approval dismissal
  const newCommitSha = await updateContextFile(diff, pr.head.ref);
  const targetCommitSha = newCommitSha || pr.head.sha;

  let reviewEvent = aiResult.verdict || "COMMENT";

  try {
    await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}/reviews`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        commit_id: targetCommitSha,
        event: reviewEvent,
        body: finalSummary,
        comments: validComments
      })
    });
  } catch (err) {
    console.warn(`Submitting review failed (${err.message}). Falling back to top-level comment review...`);

    // If inline comments were rejected by GitHub diff context or APPROVE was disallowed (e.g. 422),
    // append all inline comments into summary body and submit as a top-level COMMENT review
    const fallbackSummary = validComments.length > 0
      ? `${finalSummary}\n\n### 📝 Inline kommentarer\n` + validComments.map((c) => `* **\`${c.path}:${c.line}\`**: ${c.body}`).join("\n")
      : finalSummary;

    const fallbackBody = reviewEvent === "APPROVE"
      ? `> **Status: ✅ Approved by AI Reviewer**\n\n${fallbackSummary}`
      : fallbackSummary;

    await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}/reviews`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        commit_id: targetCommitSha,
        event: "COMMENT",
        body: fallbackBody
      })
    });
  }

  console.log(`Review submitted with verdict: ${reviewEvent}`);
}

run().catch((err) => {
  console.error("Workflow failed:", err);
  process.exit(1);
});