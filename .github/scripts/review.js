const fs = require("fs");
const path = require("path");
const { execSync } = require("child_process");

const GITHUB_TOKEN = process.env.GITHUB_TOKEN;
const API_KEY = process.env.GEMINI_API_KEY_REVIEW;
const PR_NUMBER = process.env.PR_NUMBER;
const REPO = process.env.GITHUB_REPOSITORY;
const CONTEXT_FILE_PATH = path.join(process.cwd(), "docs", "PROJECT_CONTEXT.md");

const GITHUB_API = "https://api.github.com";

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

async function requestGeminiModel(model, prompt, systemInstruction, expectJson = true) {
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
    throw new Error(`Model ${model} returned status ${response.status}: ${errText}`);
  }

  const data = await response.json();
  const rawText = data.candidates?.[0]?.content?.parts?.[0]?.text;

  if (!expectJson) {
    return rawText ? rawText.trim() : null;
  }

  const parsedJson = extractAndCleanJson(rawText);
  if (!parsedJson && rawText) {
    console.warn(`Model ${model} returned non-parsable JSON output:\n${rawText}`);
  }
  return parsedJson;
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
  -> Du SKAL eskalere til Gemini 3.8 Flash! Sæt "verdict": "ESCALATE" (eller "NEED_CONTEXT" hvis du mangler specifikke filer fra 'Repository Files').
* **Score 9-10 (Kritisk):** Dybe refactorings, nye kernemoduler, ændring af auth/sikkerhed, globale middlewares.
  -> Du SKAL sætte "verdict": "ESCALATE".

Hvis "scope_score" >= 6 eller "cross_layer_impact" er true, må du ALDRIG godkende selv. Du SKAL eskalere.`;
    verdictEnum = `"APPROVE" | "REQUEST_CHANGES" | "COMMENT" | "ESCALATE" | "NEED_CONTEXT"`;
  } else if (mode === "HEAVY_INVESTIGATE") {
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

async function performReview(basePrompt) {
  const loadedFiles = new Map();
  let escalated = false;

  // Phase 1: Fast triage with light models (LIGHT_MODELS)
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
    }
  }

  // Phase 3: Emergency fallback queue with forced review on light models (no escalation allowed)
  console.log("All heavy models failed. Falling back to light models with forced review as emergency backup...");
  const emergencyFilesText = formatLoadedFiles(loadedFiles);
  const emergencyPrompt = emergencyFilesText
    ? `${basePrompt}\n\n### Requested File Contents (Full Context from PR branch):\n${emergencyFilesText}\n`
    : basePrompt;

  for (const model of LIGHT_MODELS) {
    try {
      console.log(`Attempting emergency review with light model: ${model}...`);
      const fallbackInstruction = buildSystemInstruction("FINAL", loadedFiles.size > 0);
      const result = await requestGeminiModel(model, emergencyPrompt, fallbackInstruction, true);
      if (result && result.verdict !== "NEED_CONTEXT" && result.verdict !== "ESCALATE") {
        console.log(`✅ Review successfully completed by emergency light model: ${model}`);
        return result;
      }
    } catch (err) {
      console.warn(`Emergency fallback model ${model} failed (${err.message}). Trying next...`);
    }
  }

  throw new Error("All review models in all tiers failed.");
}

function filterDiff(diffText) {
  return diffText
    .split(/(?=^diff --git )/m)
    .filter((chunk) => !chunk.includes("docs/PROJECT_CONTEXT.md"))
    .join("");
}

function parseDiffLines(diffText) {
  const fileLinesMap = new Map();
  const fileDiffs = diffText.split(/^diff --git /m);

  for (const fileDiff of fileDiffs) {
    if (!fileDiff.trim()) continue;
    const match = fileDiff.match(/^[a-b]\/(.+?)\s+[a-b]\/(.+)/m);
    if (!match) continue;
    const filePath = match[2].trim();

    const addedLines = [];
    let currentNewLine = 0;
    const lines = fileDiff.split("\n");

    for (const line of lines) {
      const hunkHeader = line.match(/^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/);
      if (hunkHeader) {
        currentNewLine = parseInt(hunkHeader[1], 10);
        continue;
      }

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

function matchSnippetToLine(fileLinesMap, filePath, snippet) {
  const addedLines = fileLinesMap.get(filePath);
  if (!addedLines || !snippet) return null;

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

  const systemInstruction = `
You are a software architecture documentation assistant for the Bifrost project.
Your task is to evaluate if this Pull Request introduces high-level architecture changes, new modules, domain models, APIs, external integrations, or structural patterns that belong in 'docs/PROJECT_CONTEXT.md'.

Rules:
1. If the PR ONLY contains bugfixes, refactoring, documentation, tests, minor UI tweaks, or code that fits within existing architecture, return EXACTLY: [NO_CHANGES]
2. ONLY if significant new architectural concepts, domain models, entities, database tables, or modules are introduced: Output the COMPLETE updated Markdown file.
3. Write 100% in English without markdown code blocks (\`\`\`markdown ... \`\`\`).
4. Maintain high-level architectural value. Do NOT log commit histories or small implementation details.
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

    const cleanedContext = response
      .replace(/^```markdown\s*/i, "")
      .replace(/^```\s*/i, "")
      .replace(/```\s*$/i, "")
      .trim();

    if (cleanedContext && cleanedContext !== currentContext.trim()) {
      execSync(`git fetch origin ${prBranch}`);
      execSync(`git checkout -B ${prBranch} FETCH_HEAD`);

      fs.mkdirSync(path.dirname(CONTEXT_FILE_PATH), { recursive: true });
      fs.writeFileSync(CONTEXT_FILE_PATH, cleanedContext + "\n", "utf-8");

      execSync("git config user.name 'github-actions[bot]'");
      execSync("git config user.email 'github-actions[bot]@users.noreply.github.com'");
      execSync("git add docs/PROJECT_CONTEXT.md");
      execSync("git commit -m 'docs: auto-update PROJECT_CONTEXT.md [skip review]'");
      execSync(`git push origin ${prBranch}`);
      console.log(`[${usedModel}] docs/PROJECT_CONTEXT.md updated and committed to ${prBranch}.`);
      return execSync("git rev-parse HEAD", { encoding: "utf-8" }).trim();
    }
  } catch (err) {
    console.warn("Could not automatically update PROJECT_CONTEXT.md:", err.message);
  }
  return null;
}

async function run() {
  const pr = await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}`);
  const diff = await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}`, {
    headers: { "Accept": "application/vnd.github.v3.diff" }
  });

  // Ensure local repository is checked out to the PR branch commit so on-demand file reads match PR state
  try {
    console.log(`Checking out PR branch ${pr.head.ref} (${pr.head.sha})...`);
    execSync(`git fetch origin ${pr.head.ref}`, { stdio: "ignore" });
    execSync(`git checkout ${pr.head.sha}`, { stdio: "ignore" });
  } catch (err) {
    console.warn(`Could not checkout PR branch locally (${err.message}). Reading disk files will use current branch.`);
  }

  const previousComments = await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}/comments`);
  const historicalFeedback = Array.isArray(previousComments)
    ? previousComments.map((c) => `[File: ${c.path} Line: ${c.line}]: ${c.body}`).join("\n")
    : "";

  let projectContext = "";
  if (fs.existsSync(CONTEXT_FILE_PATH)) {
    projectContext = fs.readFileSync(CONTEXT_FILE_PATH, "utf-8");
  }

  const reviewDiff = filterDiff(diff);
  const repoFiles = getRepositoryFileList();

  const prompt = `
PR Title: ${pr.title}
PR Author: ${pr.user.login}

Project Context:
${projectContext || "No additional project context provided."}

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

  const aiResult = await performReview(prompt);

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
      const line = matchSnippetToLine(fileLinesMap, item.path, item.snippet);
      if (line) {
        validComments.push({
          path: item.path,
          line: line,
          side: "RIGHT",
          body: item.comment
        });
      } else {
        unmatchedComments.push(`* **${item.path}:** ${item.comment}`);
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
    if (reviewEvent === "APPROVE" && err.message.includes("422")) {
      console.warn("GitHub Actions is restricted from submitting formal APPROVE. Falling back to COMMENT review event...");
      await githubFetch(`/repos/${REPO}/pulls/${PR_NUMBER}/reviews`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          commit_id: targetCommitSha,
          event: "COMMENT",
          body: `> **Status: ✅ Approved by AI Reviewer**\n\n${finalSummary}`,
          comments: validComments
        })
      });
    } else {
      throw err;
    }
  }

  console.log(`Review submitted with verdict: ${reviewEvent}`);
}

run().catch((err) => {
  console.error("Workflow failed:", err);
  process.exit(1);
});