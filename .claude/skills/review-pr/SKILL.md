---
name: review-pr
description: Run the Mouse2Joy multi-angle review locally on a PR or the current branch. Spawns 8 review subagents (security, correctness, regression, ux, stability, convention-compliance, performance, code-reuse) using the same reviewer prompts as the CI pipeline. Reports findings in the conversation instead of posting to GitHub. Use when the user wants to review a PR before opening it, double-check a branch, or get a second opinion on changes without waiting for CI.
---

# review-pr

This is the **interactive / local** counterpart to the CI workflow at
[`.github/workflows/claude-review.yml`](../../../.github/workflows/claude-review.yml).
Same 8 reviewer angles, same prompts, but it runs from a Claude Code session
and reports findings as text in the conversation instead of posting a GitHub
review.

## When to use

Trigger this skill when the user asks for things like:

- "Review this PR" / "review PR #12" / "give me a review of branch X"
- "Check my branch before I open the PR"
- "What would the CI reviewer flag on this?"
- "Run the multi-agent review locally"

Do **not** trigger if the user just wants a quick code-quality scan of one
file or a specific question about one change — those don't need 8 angles.

## What it does

1. Resolves the target: PR number (via `gh pr view <n>`), explicit branch
   name, or "the current branch" by default.
2. Loads the 8 reviewer system prompts from
   [`.github/claude-review/reviewers/`](../../../.github/claude-review/reviewers/).
   These files are the **single source of truth** — they are also used by
   the CI workflow.
3. Lists the changed files in the diff.
4. Runs each reviewer as a `Task` subagent in turn (sequentially, not in
   parallel — the local skill optimizes for transparency in the conversation
   over throughput).
5. Aggregates findings into a single response in the conversation, with one
   section per angle.

## How to run it

Follow these steps in order. The user is in this Claude Code session; you
are the orchestrator.

### Step 1: Resolve the target

Look at the user's request:

- If they named a PR number (e.g. "review PR #8"): use that.
- If they named a branch: use `git diff <base>...<branch>` against
  `origin/main`.
- Otherwise: default to the current branch vs `origin/main`. Run
  `git rev-parse --abbrev-ref HEAD` and check that it isn't `main`. If it
  is, ask the user which branch / PR they meant.

### Step 2: Identify changed files

Run one of:

- For a PR: `gh pr view <n> --json files --jq '.files[].path'`
- For a branch: `git diff --name-only origin/main...<branch>`

Hold this list — you'll pass it to every reviewer.

If the list is empty, say so and stop.

If the list is *only* docs (`ai-docs/**`, `**/*.md`, `LICENSE`) or *only*
dependency files (`Directory.Packages.props`, `global.json`, csproj package
references), tell the user "this matches the CI gate's skip rules and
wouldn't be reviewed automatically — running anyway since you asked
explicitly" and continue.

### Step 3: Load reviewer prompts

Read all 8 files from `.github/claude-review/reviewers/`:

- `security.md`
- `correctness.md`
- `regression.md`
- `ux.md`
- `stability.md`
- `convention-compliance.md`
- `performance.md`
- `code-reuse.md`

You'll inject each one as the `Task` subagent's instructions.

### Step 4: Run reviewers sequentially

For each of the 8 reviewer prompts, invoke the `Task` tool with:

- `subagent_type: "general-purpose"`
- `description`: short (e.g. "Security review of PR #8")
- `prompt`: the full text of the reviewer prompt file, followed by:

  ```
  ---

  ## This review

  Target: <PR #N | branch <name>>
  Base: origin/main
  Head: <head ref>

  Changed files:
  <bulleted list of paths>

  Use Read / Grep / Glob / Bash (for `git diff` and `gh pr diff`) to
  investigate. Don't dump everything — pull only what you actually need
  for your angle. Output the structured JSON your instructions require.
  ```

After each subagent returns, summarize its findings into the conversation
under a short heading like `## Security (N findings)` so the user sees
progress, then move on. Don't try to render the JSON literally — turn it
into readable markdown:

- Summary as a sentence.
- Critical findings as bullets with `path:L<line>` references.
- Suggestions / nits collapsed under a "more" or omitted if low value (the
  user is reading in real-time; respect their attention).

### Step 5: Final wrap-up

After all 8 finish, post one short consolidated summary:

- "**Critical**: N findings" with a one-line description each.
- "**Suggestions worth considering**: N" — list 3–5 of the most useful.
- Token usage if you tracked it (optional).

Offer to dive deeper into any specific finding the user wants to discuss.

## Important constraints

- **Do NOT post to GitHub.** This skill is local-only. The CI workflow
  handles GitHub reviews. If the user wants the same review posted as a
  GitHub review, point them at the CI pipeline (push the branch, open
  the PR, CI runs the review automatically).
- **Sequential, not parallel.** The CI script fan-outs 8 agents in
  parallel; this skill runs them one at a time so the user sees progress
  and can interrupt. If they want speed, the CI workflow exists.
- **Reviewer prompts are the contract.** Don't paraphrase or summarize
  them when injecting into a subagent — pass the file content verbatim.
  This is what keeps CI review and local review in sync.
- **Conventions referenced by the prompts** (`CLAUDE.md`, the
  "Things that are easy to get wrong" section, `ai-docs/implementations/`)
  are read by the subagents themselves via tool calls. You don't need to
  pre-load them; the agent will reach for them when relevant.

## What this skill does NOT do

- Open PRs / push branches / merge anything.
- Modify files (it's a review, not a fix).
- Run the .NET build or tests (CI does that; this skill is post-CI in
  spirit, even if there's no formal gate).
- Replace [`/review`](https://docs.claude.com/claude-code) — that's the
  generic Claude Code review command. This skill is the Mouse2Joy-specific
  8-angle methodology.

## Example invocations

User: "Review my current branch."

You:
1. `git rev-parse --abbrev-ref HEAD` → `feature/widget-rotation`.
2. `git diff --name-only origin/main...feature/widget-rotation` → 4 files.
3. Read the 8 reviewer prompts.
4. Run each via `Task` in turn, posting per-angle summaries as you go.
5. Final consolidated summary.

---

User: "Run the multi-agent review on PR #12."

You:
1. `gh pr view 12 --json files,headRefName --jq ...`
2. Same as above, but with PR-specific context.
