// Orchestrator. Runs from .github/claude-review/ in CI (after npm ci).
//
// Required env:
//   ANTHROPIC_API_KEY  -- secret. If missing, post a "skipped" comment and exit.
//   GH_TOKEN           -- workflow GITHUB_TOKEN (used by `gh`).
//   REPO               -- e.g. "Zanges/Mouse2Joy".
//   PR_NUMBER          -- PR being reviewed.
//   PR_HEAD_SHA        -- commit to anchor the review at.
//   PR_BASE_SHA        -- base for the diff (for reference; gh resolves diff from PR_NUMBER).
//   PR_TITLE           -- for display in the review body.
//
// Optional:
//   DRY_RUN=1          -- print the payload instead of posting.
//   CLAUDE_MODEL       -- override default model.

import Anthropic from "@anthropic-ai/sdk";
import { readFileSync, readdirSync, writeFileSync, existsSync } from "node:fs";
import * as path from "node:path";
import { spawnSync } from "node:child_process";
import { runAgent, type AgentRunOutcome } from "./agent.js";
import { aggregate } from "./aggregator.js";

// Default to Opus 4.7 (per the Claude API skill's defaults — most capable model,
// best at the "spot subtle convention violations / regression risks" task that
// this pipeline exists for). CLAUDE.md is shared and cached across all 8
// agents, so the per-review cost stays bounded even on the larger model.
// Override via CLAUDE_MODEL env if cost becomes a concern (e.g. "claude-sonnet-4-6").
const DEFAULT_MODEL = "claude-opus-4-7";
const MODEL = process.env.CLAUDE_MODEL ?? DEFAULT_MODEL;

const REPO_ROOT = path.resolve(process.cwd(), "..", "..");
const REVIEWERS_DIR = path.resolve(process.cwd(), "reviewers");

interface ChangedFile { path: string; additions: number; deletions: number; status: string; }

async function main(): Promise<void> {
  const repo = required("REPO");
  const prNumber = required("PR_NUMBER");
  const prHeadSha = required("PR_HEAD_SHA");
  const prTitle = process.env.PR_TITLE ?? `PR #${prNumber}`;
  const dryRun = process.env.DRY_RUN === "1";

  if (!process.env.ANTHROPIC_API_KEY) {
    console.log("ANTHROPIC_API_KEY not set; posting skipped comment and exiting.");
    if (!dryRun) {
      postIssueComment(repo, prNumber,
        "Claude review skipped: `ANTHROPIC_API_KEY` not configured in repo secrets.");
    }
    return;
  }

  const claudeMdPath = path.join(REPO_ROOT, "CLAUDE.md");
  if (!existsSync(claudeMdPath)) {
    fail("CLAUDE.md not found at repo root; aborting review.");
  }
  const claudeMd = readFileSync(claudeMdPath, "utf8");

  const changedFiles = listChangedFiles(repo, prNumber);
  if (changedFiles.length === 0) {
    console.log("No changed files reported by gh; nothing to review.");
    return;
  }

  console.log(`Reviewing PR #${prNumber} (${changedFiles.length} files) with model ${MODEL}.`);

  const reviewerSpecs = loadReviewers();
  const angleLabels: Record<string, string> = Object.fromEntries(
    reviewerSpecs.map(r => [r.id, r.label]),
  );

  const client = new Anthropic({
    // SDK reads ANTHROPIC_API_KEY from env automatically; pass nothing.
  });

  const userPrompt = buildUserPrompt({ prNumber, prTitle, changedFiles });

  const outcomes: AgentRunOutcome[] = await Promise.all(
    reviewerSpecs.map(spec =>
      runAgent({
        client,
        model: MODEL,
        angleId: spec.id,
        reviewerSystemPrompt: spec.prompt,
        claudeMd,
        userPrompt,
        maxTurns: 20,
      }).then(o => {
        console.log(
          `[${spec.id}] turns=${o.turns} in=${o.inputTokens} out=${o.outputTokens} ` +
          `cache_write=${o.cacheCreationTokens} cache_read=${o.cacheReadTokens}` +
          (o.error ? ` ERROR=${o.error}` : ""),
        );
        return o;
      }),
    ),
  );

  const changedSet = new Set(changedFiles.map(f => f.path));
  const payload = aggregate({
    outcomes,
    commitId: prHeadSha,
    prTitle,
    angleLabels,
    changedFilePaths: changedSet,
  });

  if (dryRun) {
    console.log("DRY_RUN=1; payload follows:\n");
    console.log(JSON.stringify(payload, null, 2));
    return;
  }

  postReview(repo, prNumber, payload);
  console.log(`Posted review on PR #${prNumber} with ${payload.comments.length} inline comments.`);
}

function required(name: string): string {
  const v = process.env[name];
  if (!v) fail(`required env var ${name} is missing`);
  return v!;
}

function fail(msg: string): never {
  console.error(msg);
  process.exit(1);
}

function listChangedFiles(repo: string, prNumber: string): ChangedFile[] {
  const res = spawnSync("gh", [
    "pr", "view", prNumber, "--repo", repo,
    "--json", "files",
  ], { encoding: "utf8" });
  if (res.status !== 0) {
    fail(`gh pr view failed: ${res.stderr}`);
  }
  const parsed = JSON.parse(res.stdout) as { files: ChangedFile[] };
  return parsed.files ?? [];
}

interface ReviewerSpec { id: string; label: string; prompt: string; }

function loadReviewers(): ReviewerSpec[] {
  const files = readdirSync(REVIEWERS_DIR).filter(f => f.endsWith(".md")).sort();
  return files.map(file => {
    const id = file.replace(/\.md$/, "");
    const label = toLabel(id);
    const prompt = readFileSync(path.join(REVIEWERS_DIR, file), "utf8");
    return { id, label, prompt };
  });
}

function toLabel(id: string): string {
  // "code-reuse" -> "Code reuse"; "ux" -> "UX"; "convention-compliance" -> "Convention compliance".
  if (id === "ux") return "UX";
  return id
    .split("-")
    .map((w, i) => (i === 0 ? w.charAt(0).toUpperCase() + w.slice(1) : w))
    .join(" ");
}

function buildUserPrompt(args: {
  prNumber: string;
  prTitle: string;
  changedFiles: ChangedFile[];
}): string {
  const fileLines = args.changedFiles
    .map(f => `  - ${f.path} (${f.status}, +${f.additions}/-${f.deletions})`)
    .join("\n");

  return [
    `You are reviewing pull request #${args.prNumber}: "${args.prTitle}".`,
    "",
    `## Changed files in this PR`,
    "",
    fileLines,
    "",
    `Use the tools to pull diffs, read files, grep the repo, and list directories as needed for your specific review angle. Don't dump everything -- look at what you actually need. When you're done, emit your final response as a single JSON object matching the schema in your system prompt. No prose outside the JSON.`,
  ].join("\n");
}

function postReview(repo: string, prNumber: string, payload: unknown): void {
  // Use `gh api` so the GITHUB_TOKEN auth is reused without us re-implementing it.
  // Pipe the JSON body via stdin (--input -) to avoid argv length issues.
  const json = JSON.stringify(payload);
  const res = spawnSync("gh", [
    "api", "--method", "POST",
    `repos/${repo}/pulls/${prNumber}/reviews`,
    "--input", "-",
  ], { input: json, encoding: "utf8" });
  if (res.status !== 0) {
    // If GitHub rejects some inline comments (e.g. line not in diff), fall
    // back to posting the body as an issue comment so the review isn't lost.
    console.error(`gh api POST reviews failed: ${res.stderr}`);
    const parsed = payload as { body: string };
    if (parsed?.body) {
      console.error("Falling back to issue comment.");
      postIssueComment(repo, prNumber, parsed.body);
    } else {
      process.exit(1);
    }
  }
}

function postIssueComment(repo: string, prNumber: string, body: string): void {
  const tmp = path.join(process.cwd(), ".comment-body.tmp");
  writeFileSync(tmp, body, "utf8");
  const res = spawnSync("gh", [
    "pr", "comment", prNumber, "--repo", repo, "--body-file", tmp,
  ], { encoding: "utf8" });
  if (res.status !== 0) {
    console.error(`gh pr comment failed: ${res.stderr}`);
  }
}

main().catch(err => {
  console.error(`fatal: ${err.stack ?? err.message ?? err}`);
  process.exit(1);
});
