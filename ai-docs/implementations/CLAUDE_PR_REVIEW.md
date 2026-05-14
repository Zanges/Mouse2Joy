# Multi-Agent Claude PR Review

## Context

The PR pipeline already had CI + CodeQL + Dependabot + auto-merge + path/title labeling (see [CI_AND_RELEASES.md](CI_AND_RELEASES.md) and [CODE_QUALITY_AUTOMATION.md](CODE_QUALITY_AUTOMATION.md)). The remaining gap was *semantic* review — the things a Roslyn analyzer or pattern-matching scanner can't catch:

- CLAUDE.md conventions (no shortcuts, no hardcoded values, write-down required for non-trivial features, tests required for pure-logic surfaces).
- Foot-guns documented in CLAUDE.md "Things that are easy to get wrong" but not lintable (kernel-driver split, panic-hotkey independence, persistence schema versioning).
- Code-reuse opportunities (similar helpers across projects, near-duplicates).
- Regression risk against documented decisions in `ai-docs/implementations/`.

Goal: a gated, multi-perspective AI review that fires *after* CI succeeds, only on PRs worth reviewing, and posts ONE consolidated review with sections per angle. Useful second opinion on substantive PRs without credit burn on every Dependabot bump.

## What changed

### New workflow: `.github/workflows/claude-review.yml`

`workflow_run` listener on the existing `CI` workflow's `completed` event. Two jobs:

- **`gate`** — runs on every CI completion. Outputs `should_review: true|false`. Skips if CI failed, PR is from Dependabot, PR is draft, has the `skip-claude-review` label, diff is < 10 lines, OR diff touches only `ai-docs/**` / `**/*.md` / `LICENSE` / `Directory.Packages.props` / `global.json` / `*.csproj`-where-only-`<PackageReference>`-lines-changed. The csproj filter uses `gh pr diff -- <path>` + `awk` to inspect each line.
- **`review`** — needs `gate`, runs only when the gate passes. Checks out the PR head SHA, sets up Node 20, installs `.github/claude-review/` deps via `npm ci`, runs `npm start`. Carries PR metadata through workflow outputs.

### New review script: `.github/claude-review/`

TypeScript, runs with `tsx` (no compile step). Uses `@anthropic-ai/sdk` directly so we can run multiple agents in parallel, share cached prefixes across them, and aggregate output into a single GitHub review.

- [`index.ts`](../../.github/claude-review/index.ts) — orchestrator. Loads `CLAUDE.md`, the 8 reviewer prompts, and the list of changed files; spawns 8 agentic loops in parallel via `Promise.all`; aggregates; posts the review via `gh api`.
- [`agent.ts`](../../.github/claude-review/agent.ts) — generic agentic-loop driver. Calls `messages.create`, dispatches tool calls, feeds results back, loops until the model stops calling tools and emits its final JSON (validated against a `zod` schema).
- [`tools.ts`](../../.github/claude-review/tools.ts) — four tools exposed to every agent: `get_file_diff`, `read_file`, `grep_repo`, `list_dir`. All paths are normalized and rejected if they escape the repo root.
- [`aggregator.ts`](../../.github/claude-review/aggregator.ts) — merges N agent outputs into one `POST /repos/.../pulls/<n>/reviews` payload. Top-level body is markdown with one section per angle; inline comments are `[Angle – severity]`-prefixed and anchored to changed lines.
- [`reviewers/*.md`](../../.github/claude-review/reviewers/) — 8 system-prompt files, one per angle: `security`, `correctness`, `regression`, `ux`, `stability`, `convention-compliance`, `performance`, `code-reuse`.

Each reviewer prompt declares its scope, what NOT to comment on (so angles don't overlap), how to investigate using the tools, and a mandatory JSON output schema with three-level severity (`critical` / `suggestion` / `nit`).

### New label

`skip-claude-review` (created via `gh label create`). Applying it to a PR causes the gate job to short-circuit.

### Labeler update

[.github/labeler.yml](../../.github/labeler.yml) — added `.github/claude-review/**` under the `ci` glob, so PRs that touch the review pipeline get the `ci` label automatically.

## Key decisions

- **`workflow_run` listener, not `pull_request`**. Lets us gate on CI conclusion deterministically without polling. The known trade-off: workflow file changes only take effect after merging to `main`. Worth it — the alternative (polling check-runs from a `pull_request`-triggered workflow) is much more YAML and has timeout/retry semantics to manage.

- **Eight reviewer agents, parallel, single consolidated review**. Each agent has a tight focus, declares what it does NOT comment on, and produces structured JSON. The aggregator deduplicates the noise and posts one review with sections — much more readable than 8 separate reviews stacked on a PR. The Code-reuse and Convention-compliance agents are the highest-leverage of the eight: those are the angles a human reviewer would spot but no linter can.

- **Tool-augmented, not full-diff-dump**. Agents pull only what they need via `get_file_diff` / `read_file` / `grep_repo` / `list_dir`. Massive cost win on large PRs where each agent might only care about a few files. Also produces better reviews: the agent can read callers (`grep_repo` for usages), look at the test file (`read_file`), inspect a write-down in `ai-docs/implementations/` — context the agent decides it needs, not context the orchestrator guesses.

- **Anthropic SDK directly, not `anthropics/claude-code-action`**. The action ships one agent per invocation and is opinionated about how it posts results. To run 8 parallel agents, share a cached `CLAUDE.md` prefix across them, and aggregate findings into one unified review, we need direct SDK access. The action would have been simpler if a single reviewer was the goal.

- **Cache `CLAUDE.md` once across all 8 agents per review**. Each agent's system prompt is structured as `[reviewer-specific text] + [CLAUDE.md with cache_control: ephemeral]`. The first agent's call pays for cache creation; the other 7 hit the cache. With CLAUDE.md at ~600 lines, the cache-read price is roughly 1/10 of cache-creation, so the effective per-review cost of the convention context is ~1.7× the cost of reading CLAUDE.md once, not 8×.

- **Opus 4.7 with adaptive thinking and `effort: "high"`**. The pipeline only fires on substantive PRs (the gate filters out the trivial cases), so per-review cost is bounded — and on the angles that are uniquely well-suited to AI review (convention-compliance, code-reuse, regression risk), the model's depth matters more than per-token cost. Adaptive thinking lets the model decide how much to think per turn without us tuning `budget_tokens`. `CLAUDE_MODEL` env var overrides the default — drop to `claude-sonnet-4-6` if cost ever becomes a concern. The cached `CLAUDE.md` prefix amortizes context cost across all 8 agents regardless of which model is used.

- **Structured output via `output_config.format` (json_schema)**. Each agent's final response is constrained to the review-result schema (`summary` + `findings[]` with strict severity enum) by the API itself, not just by prompt instruction. zod still validates the parsed JSON as a belt-and-braces guard. This eliminates the "agent ignored the schema and returned prose" failure mode entirely. The JSON Schema lives next to the zod schema in [agent.ts](../../.github/claude-review/agent.ts).

- **`event: COMMENT`, never `REQUEST_CHANGES`**. The review is advisory. AI false positives are inevitable; making them merge-blockers would create more friction than the reviews catch. The user reads and acts.

- **Inline comments anchored to changed lines only**. GitHub rejects review comments on lines not in the PR's diff. The aggregator filters findings whose `path` isn't in the changed-files set; those findings get surfaced as critical bullets in the per-angle summary instead, so nothing is lost. Findings on changed files but at line numbers outside any hunk will also be silently dropped by GitHub — accepted trade-off; the agent is told the changed-line ranges via the diff and is expected to anchor near them.

- **Fallback to an issue comment if `POST /reviews` fails**. If GitHub rejects the review payload (e.g. an inline comment on an invalid line), the orchestrator posts the body as a plain PR comment. The review isn't lost; it's just less integrated.

- **Skip silently with one comment if `ANTHROPIC_API_KEY` is missing**. Lets the workflow land before the secret is configured without breaking CI or blocking PRs. The comment is one line and self-explanatory.

- **Per-agent 20-turn budget cap**. Hardcoded in `agent.ts`. If an agent gets stuck in a tool-call loop, it errors out after 20 turns and the aggregator surfaces the error in its own section. Adjust if you see legitimate reviews hitting the cap.

- **JSON schema validation via `zod`**. Each agent's final output is parsed and validated. If the model returns malformed JSON or skips required fields, the agent surfaces an error rather than posting garbage. The `extractJsonObject` helper accepts both bare JSON and `` ```json `` fences.

- **Path validation in tools**. `safeResolve` rejects any path that escapes the repo root (absolute or `..`-traversal). Defense in depth against a hypothetical prompt-injection vector where the diff itself instructs the agent to read `/etc/passwd`.

- **Three-level severity (critical / suggestion / nit)**. Five was tempting but in practice agents inflate severity and force endless adjudication. Three levels map cleanly to "must look at" / "worth considering" / "ignore unless you're polishing".

- **Reviewer-isolation rules in each prompt**. Every reviewer's "What NOT to comment on" section explicitly lists the OTHER angles. Without this, agents drift — the Security agent starts commenting on logic bugs, the Stability agent starts critiquing copy. The boundaries keep each angle's output focused and prevent N agents from all flagging the same finding from N slightly different angles.

- **TypeScript on a .NET repo**. The Anthropic TS SDK is the most mature surface. Localized to `.github/claude-review/`; doesn't leak into the .NET build. Dependencies pinned, package-lock.json committed.

## Local skill (companion to the CI pipeline)

In addition to the CI workflow, the repo ships a project-local Claude Code
skill at [`.claude/skills/review-pr/SKILL.md`](../../.claude/skills/review-pr/SKILL.md).
This is the *interactive* counterpart: same 8 reviewer angles, same prompts,
but invoked from a Claude Code session against a local branch or open PR,
with findings reported in the conversation instead of posted to GitHub.

### Why both

The CI pipeline is the right shape for "automatically review every gated PR
with strong cost control and structured GitHub output". It is NOT the right
shape for "I'm on a branch and want a quick second opinion before opening
the PR" — there's no PR to attach the review to, you don't want a GitHub
round-trip, and you want to ask follow-up questions in the same session.
The local skill covers that second use case.

### Why a skill, not the Agent SDK

We considered rewriting the whole pipeline on top of the Claude Agent SDK
so one implementation could serve both CI and interactive use. The reason
we didn't: the CI design's *value comes from* its parallel-fan-out with
shared prompt cache, structured JSON validation via zod, and unified
review aggregation. Collapsing it onto a single sequential agent loop
(which is what an Agent SDK rewrite naturally produces) would either lose
those properties or rebuild them on top of a heavier dependency. Two
small focused tools beat one big general one here.

### Single source of truth for the prompts

Both the CI pipeline and the local skill load the 8 reviewer system
prompts from the same place: `.github/claude-review/reviewers/*.md`.
Editing a prompt benefits both flows. The CI script reads them via Node
filesystem APIs; the skill instructs the orchestrating Claude Code
session to Read them and pass them verbatim into `Task` subagents.

### How the skill differs from CI

| Aspect | CI workflow | Local skill |
|---|---|---|
| Trigger | Automatic, on CI success | User invokes from a Claude Code session |
| Concurrency | 8 agents in parallel | 8 agents sequentially |
| Prompt cache | Yes (shared CLAUDE.md prefix) | No (each Task subagent is a fresh context) |
| Output | GitHub review (inline + summary) | Conversation messages |
| Gate | Strict (skips Dependabot / docs / etc.) | Honors the same gate but warns and continues if the user explicitly asks |
| Side effects | Posts to PR | None |

## Files touched

### Added

- [.github/workflows/claude-review.yml](../../.github/workflows/claude-review.yml)
- [.github/claude-review/package.json](../../.github/claude-review/package.json), [package-lock.json](../../.github/claude-review/package-lock.json), [tsconfig.json](../../.github/claude-review/tsconfig.json)
- [.github/claude-review/index.ts](../../.github/claude-review/index.ts)
- [.github/claude-review/agent.ts](../../.github/claude-review/agent.ts)
- [.github/claude-review/tools.ts](../../.github/claude-review/tools.ts)
- [.github/claude-review/aggregator.ts](../../.github/claude-review/aggregator.ts)
- [.github/claude-review/reviewers/security.md](../../.github/claude-review/reviewers/security.md)
- [.github/claude-review/reviewers/correctness.md](../../.github/claude-review/reviewers/correctness.md)
- [.github/claude-review/reviewers/regression.md](../../.github/claude-review/reviewers/regression.md)
- [.github/claude-review/reviewers/ux.md](../../.github/claude-review/reviewers/ux.md)
- [.github/claude-review/reviewers/stability.md](../../.github/claude-review/reviewers/stability.md)
- [.github/claude-review/reviewers/convention-compliance.md](../../.github/claude-review/reviewers/convention-compliance.md)
- [.github/claude-review/reviewers/performance.md](../../.github/claude-review/reviewers/performance.md)
- [.github/claude-review/reviewers/code-reuse.md](../../.github/claude-review/reviewers/code-reuse.md)
- [.claude/skills/review-pr/SKILL.md](../../.claude/skills/review-pr/SKILL.md) — local-skill counterpart (see "Local skill" section above).

### Modified

- [.github/labeler.yml](../../.github/labeler.yml) — `.github/claude-review/**` added to the `ci` label glob.

### Deliberately unchanged

- [.github/workflows/ci.yml](../../.github/workflows/ci.yml) — the new workflow listens for CI completion via `workflow_run`; CI itself doesn't need to know the review exists.
- [.github/dependabot.yml](../../.github/dependabot.yml) — already correctly excludes review credit burn (Dependabot is one of the gate's skip conditions).

## Manual setup the user must do

1. **Add the repo secret `ANTHROPIC_API_KEY`** in Settings → Secrets and variables → Actions. Without it, the workflow runs but posts a one-line "skipped" comment instead of failing.
2. No other repo-setting toggles. (Unlike the auto-merge workflow, this doesn't need `allow_auto_merge`.)

## Verification plan

End-to-end, after merging:

1. **Local typecheck** — `cd .github/claude-review && npm ci && npm run typecheck` should pass. (Verified at check-in.)
2. **No-key smoke** — running `npm start` with `ANTHROPIC_API_KEY=""` should print "skipped" and exit 0. (Verified at check-in.)
3. **Gate on docs-only PR** — open a PR touching only `ai-docs/**`. After CI passes, the gate should output `should_review=false` with reason "all changed files are docs / dependency-only". No review posted. Check workflow logs.
4. **Gate on Dependabot PR** — the next Dependabot PR should also be gated out (author check). Already covered by the existing auto-merge flow, but verify the new review workflow correctly skips.
5. **Real review** — open a small substantive PR touching `src/`. After CI succeeds, the review workflow fires, 8 agents run, one consolidated review appears within ~3 minutes. Verify:
   - Single review on the PR, event `COMMENT`.
   - Body has 8 sections (one per angle).
   - Some inline comments anchored to specific lines, prefixed with `[Angle – severity]`.
   - Token-usage footer is present.
6. **Cost spot-check** — check Anthropic console after the real review. If total tokens > 500k on a small PR, revisit caching wiring (cache-read tokens should be ≥ 7 × cache-creation tokens).
7. **Skip-label opt-out** — apply `skip-claude-review` to an open PR, push a new commit, wait for CI. Gate should skip with reason "PR has skip-claude-review label".
8. **Missing-key behavior** — temporarily remove `ANTHROPIC_API_KEY` on a test branch (not main), push, observe one-line skip comment, restore the secret.
9. **Local skill** — open a Claude Code session in the repo, on a non-`main` branch with at least one source file changed, and ask "review my current branch". The skill should resolve the branch, load the 8 reviewer prompts, run each as a `Task` subagent, and report a consolidated summary in the conversation. No GitHub review should be posted.

## Follow-ups

- **Cost ceiling per PR**: if a single PR ever costs more than, say, $1, that's a sign agents are over-tool-calling or running into a pathological case. Add a hard cap in `agent.ts` based on cumulative input tokens, not just turn count.
- **Per-PR triage**: this v1 runs all 8 agents on every gated PR. A future v2 could pick a subset based on what the PR touches (skip UX agent if no UI files; skip Performance if no Engine changes). The labeler already encodes this signal in labels — re-use it.
- **Review re-runs**: today, a new push triggers CI which re-triggers the review, posting a second review. GitHub keeps both. A v2 could dismiss the old review (`gh api ... -X PUT /reviews/<id>/dismissals`) to keep the PR clean.
- **`@claude` follow-up bot**: optionally add `anthropics/claude-code-action@v1` for on-demand questions in PR comments (e.g. "@claude is this thread-safe?"). Complementary, not redundant.
- **Workflow file changes only via main**: because of `workflow_run`, iterating on `claude-review.yml` itself requires merging changes to main first. Annoying but acceptable; usually the script and reviewer prompts are what changes, not the workflow YAML.
- **Tooling addition note**: this is the first Node toolchain in the repo. If we add a second one in the future, consider consolidating; for now `.github/claude-review/` owns its own deps so it can't drift with anything else.
