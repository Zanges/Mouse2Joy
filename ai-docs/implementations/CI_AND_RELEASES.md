# CI and Automatic Releases

## Context

Before this change Mouse2Joy had no CI, no branch protection, no release automation, and no versioning scheme. Every build was a manual `dotnet publish` on the dev machine. Goal: a low-maintenance pipeline where `main` is always green, every PR gets fast feedback, and cutting a release is part of the PR itself — no extra commands, no separate tagging step.

## What changed

- Added a `VERSION` file at the repo root (single-line semver, initial value `0.1.0`). This is the single source of truth for the published version.
- Added three GitHub Actions workflows:
  - `.github/workflows/ci.yml` — on PR / push to `main`: restore → format check → Release build → tests with coverage → CodeQL. Two jobs (`build-test`, `codeql`) which become the required status checks for branch protection.
  - `.github/workflows/codeql-scheduled.yml` — Monday 06:00 UTC, `security-and-quality` query suite, decoupled from PR feedback.
  - `.github/workflows/release.yml` — on push to `main`: read `VERSION`, skip if unchanged from `HEAD~1`, else publish single-file self-contained exe, stage portable zip, install Inno Setup via Chocolatey, build installer, produce SHA256 sidecars, push `v<version>` tag, create GitHub Release as pre-release with auto-generated notes.
- Added `.github/release.yml` to categorize auto-generated release notes (Features / Bug Fixes / Dependencies / Internal / Other).
- Added `.github/dependabot.yml` for weekly grouped NuGet + Actions updates. Dependabot PRs don't bump `VERSION`, so they merge silently without cutting a release.
- Added PR template (with a "Bumped VERSION?" checkbox), bug-report and feature-request issue templates, and `config.yml` to disable blank issues.
- Added `build/installer/Mouse2Joy.iss` (Inno Setup script with Pascal-Script prereq detection — warns but allows install if ViGEmBus or Interception is missing) and `build/installer/README-FIRST.txt` (the file bundled in both the zip and the installer).
- Ran `dotnet format` once across the solution to clean up pre-existing whitespace drift in 10 files (mostly in the larger UI / Engine test files). Necessary so the new CI format gate doesn't fail on its first run. No semantic changes.

## Key decisions

- **Release trigger is `push` to `main` (i.e. PR merge), gated by `VERSION` change.** Per the user's preference, 99% of merges *should* ship a release; tying release intent to the version-bump line in the PR diff makes the choice deliberate without requiring a separate command. PRs that don't bump (docs, refactors, CI tweaks, dependabot) merge normally with no release.
- **VERSION file over GitVersion / Nerdbank.GitVersioning.** Plain text, in the PR diff, no tooling dependency. Easier to reason about; easier to override; reviewable.
- **Version flows into MSBuild via `-p:Version=` at publish time.** csproj stays version-less. No `<Version>` field to keep in sync.
- **Tag-collision check before publish.** If `VERSION` somehow points at a value that already has a `v<version>` tag (e.g. reverted then re-bumped), the workflow fails fast with a clear error before any artifacts are built. Prevents accidentally re-shipping a number.
- **Releases are pre-release until manually promoted.** Tag + artifacts + auto-generated notes are created automatically, but the release is marked pre-release. The user smoke-tests, edits notes if needed, then clicks "Set as latest release". Bad builds are never silently public.
- **Both portable zip and Inno installer.** The user explicitly wants both. The zip is for portable users; the installer registers Start Menu + uninstaller, runs prereq detection, and matches the polish expectation for desktop apps. Same Mouse2Joy.exe inside both — single publish output, repackaged two ways.
- **Installer prereq detection warns but allows.** Matches the app's existing graceful-degradation philosophy (Setup tab already reports missing drivers post-install). Detection is best-effort registry probing — `HKLM\SYSTEM\CurrentControlSet\Services\ViGEmBus` (and `ViGEmBus3` for newer nefarius builds) for ViGEm, and `HKLM\SOFTWARE\Interception` plus the keyboard class upper-filter for Interception. If detection misses, worst case is a spurious warn — the install still proceeds, and the in-app Setup tab catches it.
- **Native `interception.dll` redistribution is fine** because the user-mode shim is LGPL and we ship it next to the exe with `THIRD_PARTY_NOTICES.md`. The kernel driver is NOT bundled — users install it themselves per the existing README flow.
- **Branch protection on `main` is enforced via GitHub UI**, not code. Required checks: `build-test` and `codeql`. Linear history, no force-push, no deletion, no review count requirement (solo project). Tag protection rule on pattern `v*` to prevent tag rewrites.
- **`fail_on_unmatched_files: true`** on the release-upload action so the workflow fails loudly if any expected artifact is missing, rather than publishing an incomplete release.
- **Concurrency groups**: CI cancels in-progress runs on the same ref (saves runner minutes on rapid pushes); release does NOT cancel in-progress runs (don't kill a half-uploaded release).
- **`paths` filter on `release.yml`** is a fast pre-filter (skip the runner spin-up entirely for changes to `tests/`, `ai-docs/`, etc), but the in-job `VERSION` diff check is the authoritative gate. Belt-and-suspenders.
- **CodeQL `build-mode: manual`** because automatic mode doesn't always discover the right entry points in a WPF + multi-project layout; explicit `dotnet build` is more reliable.
- **Initial `VERSION = 0.1.0`** — first release after this lands will be `v0.1.0` if you bump in a follow-up PR. The PR that introduces CI itself is intentionally a "no bump" PR — the CI infra is internal, not a feature.

## Files touched

- Added [VERSION](VERSION)
- Added [.github/workflows/ci.yml](.github/workflows/ci.yml), [.github/workflows/codeql-scheduled.yml](.github/workflows/codeql-scheduled.yml), [.github/workflows/release.yml](.github/workflows/release.yml)
- Added [.github/release.yml](.github/release.yml) (notes config)
- Added [.github/dependabot.yml](.github/dependabot.yml)
- Added [.github/PULL_REQUEST_TEMPLATE.md](.github/PULL_REQUEST_TEMPLATE.md), [.github/ISSUE_TEMPLATE/bug_report.md](.github/ISSUE_TEMPLATE/bug_report.md), [.github/ISSUE_TEMPLATE/feature_request.md](.github/ISSUE_TEMPLATE/feature_request.md), [.github/ISSUE_TEMPLATE/config.yml](.github/ISSUE_TEMPLATE/config.yml)
- Added [build/installer/Mouse2Joy.iss](build/installer/Mouse2Joy.iss), [build/installer/README-FIRST.txt](build/installer/README-FIRST.txt)
- Reformatted (whitespace only) 10 source files across the solution via `dotnet format` — no semantic changes, just bringing the tree to a state where the CI format gate passes
- **Deliberately unchanged**: [src/Mouse2Joy.App/Mouse2Joy.App.csproj](src/Mouse2Joy.App/Mouse2Joy.App.csproj) already has correct `PublishSingleFile` / `SelfContained` / `RuntimeIdentifier=win-x64` properties and the conditional `interception.dll` copy. Version field intentionally omitted — version flows in from `VERSION` at publish time.

## Manual steps still required (cannot be scripted)

Done once, via the GitHub web UI:

1. **Settings → Branches → Branch protection rules → Add rule** for `main`:
   - Pattern: `main`
   - ✅ Require a pull request before merging (no review count)
   - ✅ Require status checks to pass: select `Build & Test` and `CodeQL` (they appear after the first CI run)
   - ✅ Require branches to be up to date before merging
   - ✅ Require linear history
   - ✅ Block force pushes
   - ✅ Block deletions
2. **Settings → Tags → New rule**:
   - Pattern: `v*`

## Follow-ups

- Code-signing the installer and the exe (paid Authenticode cert; out of scope for v1).
- README: add a short "Releases" subsection linking to the GitHub Releases page.
- Auto-update inside the app (Velopack / Squirrel) — defer until release cadence justifies it.
- Pre-merge dry-run of `release.yml` against PRs (would catch a stale `VERSION` value before the merge instead of at release time).
- Signed commits requirement on branch protection — decide after living with the current rules.
- Empty test project `tests/Mouse2Joy.VirtualPad.Tests` produces a "No test is available" warning on every `dotnet test` run; either add a smoke test or drop the project. Not related to this change, but visible in CI logs.
