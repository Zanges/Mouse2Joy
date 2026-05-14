# Automated Code Quality + PR Workflow

## Context

CI / dependabot / release automation were already in place per [CI_AND_RELEASES.md](CI_AND_RELEASES.md), but several gaps were visible:

- Dependabot was opening PRs the user had to label / merge by hand. The labels referenced in `.github/dependabot.yml` (`dependencies`, `ci`) and `.github/release.yml` (`feature`, `fix`, `internal`, `refactor`, `chore`, `tests`, `docs`, `skip-changelog`) did not exist in the repo, so Dependabot was posting "label X could not be found" comments on every PR (visible on [#8](https://github.com/Zanges/Mouse2Joy/pull/8)).
- No process for auto-merging low-risk dependency bumps after CI passed -- six Dependabot PRs were piled up at the time of writing.
- No automatic PR labeling, so the release-notes categorization in `.github/release.yml` could only work if labels were applied manually.
- Code-quality enforcement was limited to `dotnet format --verify-no-changes` + `TreatWarningsAsErrors`. The Roslyn analyzer suite was at the SDK default (`AnalysisLevel` unset; analyzers run but most rules at Info severity), so a lot of catchable issues were silently allowed.

Goal: minimal-maintenance, defaults-on automation that raises the quality bar without becoming churn.

## What changed

### Labels

Created the labels Dependabot and the release-notes config already reference, plus a project-area set for the path-based labeler. Created via `gh label create`:

- Process / release-notes: `dependencies`, `ci`, `feature`, `fix`, `internal`, `refactor`, `chore`, `tests`, `docs`, `skip-changelog`, `build`.
- Project area: `ui`, `engine`, `input`, `persistence`, `app`, `virtualpad`.

### Dependabot auto-merge workflow

New [.github/workflows/dependabot-automerge.yml](../../.github/workflows/dependabot-automerge.yml). On any Dependabot PR:

1. Runs `dependabot/fetch-metadata@v2` to read the update type.
2. If the update type is `version-update:semver-patch` or `version-update:semver-minor`, OR if it's a security update (`alert-state` is set), approves the PR and enables GitHub's native auto-merge (`gh pr merge --auto --squash`).
3. For `version-update:semver-major` or anything else, posts a comment explaining the PR needs manual review.

Auto-merge waits for required status checks (`build-test`, `codeql`) before merging, so this is safe -- it just removes the manual click.

### PR auto-labeling

Two-step labeling on every PR open/sync/reopen:

- [.github/labeler.yml](../../.github/labeler.yml) -- path-to-label map consumed by [.github/workflows/labeler.yml](../../.github/workflows/labeler.yml) (path-labeler job, `actions/labeler@v5`). Sets project-area labels (`ui`, `engine`, ...) plus `tests`/`docs`/`build`/`ci` based on which directories the PR touches.
- The same workflow has a `title-labeler` job that parses the PR title for a Conventional Commit prefix (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`, `ci:`, `build:`, `perf:`, `style:`, `revert:`) and applies the matching release-notes label.

Both jobs use `sync-labels: false` so manually-applied labels are preserved and labels are not removed when the diff later changes.

### Stricter analyzers

[Directory.Build.props](../../Directory.Build.props):

- `<AnalysisLevel>latest-recommended</AnalysisLevel>` + `<AnalysisMode>Recommended</AnalysisMode>` -- enable the curated Microsoft ruleset for .NET 8.
- `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` -- IDE0xxx style rules now fail the build (since `TreatWarningsAsErrors=true` was already set).
- `<GenerateDocumentationFile>true</GenerateDocumentationFile>` -- required so IDE0005 (remove unused usings) runs at build time. CS1591 stays in `NoWarn` so the xml emits without flooding.

New root [.editorconfig](../../.editorconfig) tunes severities (see "Key decisions"). Notable: enforces CRLF, sets indentation per file type, and pins the rule-severity overrides that make the curated set workable on a desktop WPF + native-interop codebase.

### Source fixes surfaced by the new analyzers

The full sweep flushed 140+ findings. Auto-fixable bulk (~90 IDE0011 add-braces, ~30 IDE0005 unused-usings, several IDE0044 add-readonly) was applied with `dotnet format style`. The substantive findings:

- **Real correctness fixes**:
  - [WaitableTickTimer](../../src/Mouse2Joy.Engine/Threading/WaitableTickTimer.cs) was discarding the result of `WaitForSingleObject`. If the wait failed (`WAIT_FAILED` -- e.g. handle invalidated), `WaitFor` returned immediately and the engine tick would silently degrade. Now checks for `WAIT_OBJECT_0` and falls through to the Stopwatch loop on anything else. (CA1806)
  - [InterceptionInputBackend.ForwardStroke](../../src/Mouse2Joy.Input/InterceptionInputBackend.cs) was discarding the count of strokes actually forwarded by `InterceptionNative.Send`. A return of 0 (driver context invalidated) was a silent input drop; now logs a warning. (CA1806)
  - [MonitorEnumerator.GetDpiForMonitor](../../src/Mouse2Joy.UI/Interop/MonitorInfo.cs) result is now checked; only assigns the out params on `S_OK`. Behavior unchanged (the 96/96 fallback was already correct) but now explicit. (CA1806)
  - [WindowStyles.MakeOverlay](../../src/Mouse2Joy.UI/Interop/WindowStyles.cs) now traces a Debug.WriteLine on `SetWindowLong` failure. (CA1806)

- **Mechanical correctness**: CA1725 renamed `dc` -> `drawingContext` on 10 WPF `OnRender` overrides; CS1574 / CS1573 / CS0419 doc-comment fixes (broken `cref`s and one missing `<param>` tag).

- **Suppressions** (with rationale in source): IDE0051 on Win32 constant blocks where we keep the unused half of a paired flag set for documentation; CA1051 on `Signal` (hot-path readonly struct with public readonly fields is the point); CA1720 on `OptionKind` (the enum values intentionally match primitive type names); CA1859 file-scoped on `WidgetEditorWindow.xaml.cs` (`BuildXxx` builders return `FrameworkElement` for polymorphic composition); CA1001 on `App` (WPF lifecycle is via `OnExit`, not `IDisposable`); CA1838 on the one P/Invoke StringBuilder param.

### Manual step required

Auto-merge needs **`Settings -> General -> Pull Requests -> Allow auto-merge`** turned on at the repo level. The new workflow runs but auto-merge calls will no-op until this is enabled. The Claude Code sandbox refused to flip this via API (treated it as an unauthorized config change to shared infra), so it's a one-click manual step.

## Key decisions

- **Auto-merge scope: patch + minor across all groups, plus security updates of any type.** Major bumps still require manual review. This matches the user's signal-to-noise preference and trusts the test suite for the dependency tiers Dependabot ships in this repo (Microsoft.Extensions, test SDK, xUnit, etc.). The decision is encoded in the `case` block in `dependabot-automerge.yml` so it's auditable; relaxing or tightening is a one-line edit.

- **Labeler runs on `pull_request_target`, not `pull_request`.** Required for write tokens on PRs from Dependabot. The labeler scripts only read PR metadata and apply labels -- no code execution from the PR -- so this is the safe-by-design use of `pull_request_target`.

- **`sync-labels: false`** on the path labeler. Without this, the labeler would *remove* labels when files no longer match -- so a manually-added `bug` label could vanish on the next push. False means "add only, never strip", which matches how a human labeler thinks.

- **Belt-and-braces labeling (path + title).** The path labeler gives deterministic, structural signal (touching `tests/` -> `tests` label). The title labeler gives intent signal (`feat:` -> `feature`). Both flow into the release-notes categorization. Either alone leaves gaps: path-only misses "this is a feature" if the feature lives in one file; title-only misses the area-tagging since not all PRs have Conventional Commit prefixes.

- **`AnalysisLevel=latest-recommended`, not `latest-all`.** The `-all` mode includes stylistic rules with sharp opinions (e.g. always-use-`var`, no-multi-line-statements) that don't add quality on an existing codebase -- they just generate noise. `-recommended` is Microsoft's curated subset for "default-on quality" and is the right tier for an established project adopting analyzers mid-stream.

- **Severities tuned in `.editorconfig`, not Directory.Build.props.** Editorconfig lets the test projects override individual rule severities (e.g. IDE0051 silenced under `**/*.Tests/**.cs` for paired test helpers, CA1859 silenced for tests where the abstraction is intentional). Build.props sets the *level*; editorconfig sets per-rule policy on top.

- **Per-site `#pragma warning disable` for intentional rule violations**, not blanket .editorconfig silencing. Where a rule is wrong in *one* place but right elsewhere -- Win32 constant docs in P/Invoke files, the `App` class needing CA1001 silenced because of WPF's lifecycle, etc. -- the suppression lives in source with a one-line "why" comment. This keeps the rule firing for the next legitimate case while making each existing exception reviewable in code review.

- **`GenerateDocumentationFile=true` is the price of IDE0005 on build.** Roslyn does not run IDE0005 (remove unused usings) on build without doc-file generation; this is a known [Roslyn issue](https://github.com/dotnet/roslyn/issues/41640). Generating the .xml is harmless -- nothing consumes it -- and the no-XML-doc rule (CS1591) was already in `NoWarn`.

- **Auto-merge workflow does NOT call `gh pr merge --merge` / `--rebase`.** Uses `--auto --squash`. Squash matches the repo's existing merge style (`allow_squash_merge: true`, no rebase setting), and `--auto` gates the merge on required status checks rather than merging immediately.

## Files touched

### Added

- [.github/workflows/dependabot-automerge.yml](../../.github/workflows/dependabot-automerge.yml)
- [.github/workflows/labeler.yml](../../.github/workflows/labeler.yml)
- [.github/labeler.yml](../../.github/labeler.yml)
- [.editorconfig](../../.editorconfig)

### Modified -- build / config

- [Directory.Build.props](../../Directory.Build.props) -- analyzer settings + doc-file generation

### Modified -- source (substantive)

Real behavior changes / log-on-failure additions:

- [src/Mouse2Joy.Engine/Threading/WaitableTickTimer.cs](../../src/Mouse2Joy.Engine/Threading/WaitableTickTimer.cs)
- [src/Mouse2Joy.Input/InterceptionInputBackend.cs](../../src/Mouse2Joy.Input/InterceptionInputBackend.cs)
- [src/Mouse2Joy.UI/Interop/MonitorInfo.cs](../../src/Mouse2Joy.UI/Interop/MonitorInfo.cs)
- [src/Mouse2Joy.UI/Interop/WindowStyles.cs](../../src/Mouse2Joy.UI/Interop/WindowStyles.cs)

Doc-comment fixes (`cref` / param tags):

- [src/Mouse2Joy.Engine/Modifiers/Evaluators/ParametricCurveEvaluator.cs](../../src/Mouse2Joy.Engine/Modifiers/Evaluators/ParametricCurveEvaluator.cs)
- [src/Mouse2Joy.Engine/State/EngineStateSnapshot.cs](../../src/Mouse2Joy.Engine/State/EngineStateSnapshot.cs)
- [src/Mouse2Joy.Input/CompositeInputBackend.cs](../../src/Mouse2Joy.Input/CompositeInputBackend.cs)
- [src/Mouse2Joy.UI/Controls/NumericUpDown.xaml.cs](../../src/Mouse2Joy.UI/Controls/NumericUpDown.xaml.cs)
- [src/Mouse2Joy.UI/Controls/PlaceholderText.cs](../../src/Mouse2Joy.UI/Controls/PlaceholderText.cs)
- [src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs)
- [src/Mouse2Joy.UI/Overlay/Widgets/StatusWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/StatusWidget.cs)
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs)

CA1725 parameter renames in WPF `OnRender` overrides (`dc` -> `drawingContext`):

- [src/Mouse2Joy.UI/Controls/ChainPreviewControl.cs](../../src/Mouse2Joy.UI/Controls/ChainPreviewControl.cs), [src/Mouse2Joy.UI/Controls/CurveEditorCanvas.cs](../../src/Mouse2Joy.UI/Controls/CurveEditorCanvas.cs)
- [src/Mouse2Joy.UI/Overlay/Widgets/AxisWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/AxisWidget.cs), [BackgroundWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/BackgroundWidget.cs), [ButtonGridWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/ButtonGridWidget.cs), [ButtonWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/ButtonWidget.cs), [EngineStatusIndicatorWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/EngineStatusIndicatorWidget.cs), [MouseActivityWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/MouseActivityWidget.cs), [StatusWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/StatusWidget.cs), [TwoAxisWidget.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/TwoAxisWidget.cs)

Per-site `#pragma warning disable` with rationale (suppressions for rules that don't fit a specific call site):

- [src/Mouse2Joy.App/App.xaml.cs](../../src/Mouse2Joy.App/App.xaml.cs) (CA1001 -- WPF lifecycle)
- [src/Mouse2Joy.App/PanicHotkey.cs](../../src/Mouse2Joy.App/PanicHotkey.cs) (IDE0051 -- Win32 flag-set docs)
- [src/Mouse2Joy.Input/LowLevelKeyboardBackend.cs](../../src/Mouse2Joy.Input/LowLevelKeyboardBackend.cs) (IDE0051 -- Win32 message-set docs)
- [src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs](../../src/Mouse2Joy.UI/Overlay/Widgets/OptionDescriptor.cs) (CA1720 -- intentional type-name enum values)
- [src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/WidgetEditorWindow.xaml.cs) (CA1859 file-scoped -- polymorphic builders)
- [src/Mouse2Joy.UI/ViewModels/BindingDisplay.cs](../../src/Mouse2Joy.UI/ViewModels/BindingDisplay.cs) (CA1838 -- one-shot UI thread P/Invoke)

### Modified -- source (mechanical)

~120 files received auto-fix passes from `dotnet format style` (IDE0011 add-braces, IDE0005 unused-usings, IDE0044 add-readonly) and `dotnet format whitespace` (CRLF normalization driven by the new `.editorconfig`). No semantic changes -- pure formatting / brace insertion / using-list trimming.

### Deliberately unchanged

- [.github/workflows/ci.yml](../../.github/workflows/ci.yml), [codeql-scheduled.yml](../../.github/workflows/codeql-scheduled.yml), [release.yml](../../.github/workflows/release.yml) -- the existing CI / CodeQL / release pipelines already do what's needed. The new analyzer enforcement runs *inside* the existing `Build (Release, warnings-as-errors)` step automatically, no workflow change required.
- [.github/dependabot.yml](../../.github/dependabot.yml) -- already correctly configured; the missing label problem was on the repo side, not in the YAML.
- [.github/release.yml](../../.github/release.yml) -- already references the right labels; only the labels themselves were missing.

## Follow-ups

- **Repo setting**: `Settings -> General -> Pull Requests -> Allow auto-merge` must be enabled by the user (manual UI step). The auto-merge workflow runs but its `gh pr merge --auto` calls will no-op until then.
- **Optional**: branch protection on `main` could add the new `labeler` and `dependabot-automerge` workflows as informational, but they're not "required checks" -- a Dependabot PR that fails labeler still merges. Leaving alone.
- **Optional**: a coverage-on-PR comment via ReportGenerator was considered but deferred -- not enough signal until a coverage target exists.
- **Pre-existing**: empty `tests/Mouse2Joy.VirtualPad.Tests` still prints "No test is available" on every `dotnet test` run. Inherited from [CI_AND_RELEASES.md](CI_AND_RELEASES.md) follow-ups; unchanged here.
- **Rule re-tuning**: the silenced rules in `.editorconfig` are conservative -- if a future refactor makes some of them non-issues (e.g. removing the `Signal` struct's public fields), the corresponding silence can come out and the rule re-engages elsewhere automatically.
