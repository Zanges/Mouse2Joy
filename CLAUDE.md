# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this project is

Mouse2Joy is a Windows desktop app (.NET 8 / WPF, `net8.0-windows`, x64) that emulates an Xbox 360 / XInput gamepad and maps mouse motion, mouse buttons, scroll, and keyboard keys to virtual gamepad outputs. It's a solo project; v1 is functional end-to-end.

For the full architecture write-up — solution layout, tech stack, lifecycle, "where things live" table, and the rationale behind key design choices — read [INITIALWORK.md](ai-docs/implementations/INITIALWORK.md). That document is the source of truth for "big picture" architecture; do not duplicate its contents here.

Past feature implementations are documented under [ai-docs/implementations/](ai-docs/implementations/). Read the relevant write-down before touching an area you haven't worked on recently — it captures decisions and trade-offs that the code itself doesn't explain.

## KEY IMPORTANT CONVENTIONS
- **Always ask clarifying questions; never assume.** If anything is ambiguous, or if you see multiple ways to implement something, ask the user what they prefer. Don't guess or pick the "obvious" option — the user has specific preferences that may not match your assumptions. Especially design and implementation decisions have to be decided by the user. The user is happy to be consulted.
- **You should however feel empowered to make suggestions and recommendations** based on your expertise, but frame them as suggestions and ask for the user's input. For example, "I see two ways to implement this: A and B. A is simpler but less flexible; B is more complex but future-proof. Which do you prefer?" - this invites the user to make an informed choice without having to come up with options themselves.
- **Don't commit or push any code without explicit permission.** Always ask the user to review and approve your changes before committing them to the repository. The user wants to maintain control over the codebase and ensure that all changes align with their vision for the project.
- **No Shortcuts**: Always implement the full feature or fix, even if it seems "obvious". Don't take shortcuts or make assumptions about what's "obviously right". The user prefers explicit, complete implementations.
- **No Hacks**: Don't write "temporary hacks" or "just this once" code. If it needs to be done, do it properly. The user prefers clean, maintainable code over quick fixes.
- **Keep everything user configurable**: Don't hardcode values or behaviors that the user might want to change. If it's a tweakable setting, make it a user option with a reasonable default.
- **Document every feature in `ai-docs/implementations/`**: After finishing any non-trivial feature or behavior change, write a concise Markdown write-down to `ai-docs/implementations/<FEATURE_NAME>.md` (UPPER_SNAKE_CASE filename). This is the persistent record future work depends on. Use this template:

  ```markdown
  # <Feature title>

  ## Context
  Why this change exists — the problem, what prompted it, intended outcome.

  ## What changed
  Bullet list of concrete changes (UI, model, behavior).

  ## Key decisions
  Choices made and *why* — especially trade-offs, deferred work, or non-obvious behavior. This is the highest-value section; future implementations will read it to avoid re-litigating settled questions.

  ## Files touched
  List with relative-path Markdown links. Note any "deliberately unchanged" files where it might be surprising.

  ## Follow-ups
  Known deferred work, half-finished placeholders, or follow-on features that this change set up.
  ```

  Keep it scannable — roughly one screen. Read existing write-downs in that folder before making changes to an area already documented to keep decisions consistent. old decisions are not set in stone, but changing them should be a conscious choice, not an accidental consequence of "just doing the thing".

## Common commands

```powershell
# Build everything
dotnet build Mouse2Joy.sln

# Run the host (WPF exe). MUST be in an elevated shell — Interception needs admin to attach.
dotnet run --project src/Mouse2Joy.App

# Run all tests
dotnet test Mouse2Joy.sln

# Run a single test project / test
dotnet test tests/Mouse2Joy.Engine.Tests
dotnet test --filter "FullyQualifiedName~CurveEvaluatorTests.Evaluate_AppliesDeadzone"

# Release publish
dotnet publish src/Mouse2Joy.App -c Release
```

The app itself must run as Administrator for Interception to attach. Without admin, the UI still opens and the Setup tab reports what's missing — useful when iterating on UI without needing capture working.

Build settings (from [Directory.Build.props](Directory.Build.props)): nullable enabled, `TreatWarningsAsErrors=true` (only `CS1591` is downgraded), central package management via [Directory.Packages.props](Directory.Packages.props). SDK pinned to 8.0.303 with `latestFeature` rollforward in [global.json](global.json).

## Things that are easy to get wrong

- **Two kernel drivers, two different jobs.** ViGEmBus = output (virtual pad). Interception = mouse capture only. Keyboard capture is a user-mode `WH_KEYBOARD_LL` hook. They are NOT interchangeable, and this split is load-bearing — see the "Critical insight on input capture" section in [INITIALWORK.md](ai-docs/implementations/INITIALWORK.md) for why. Synthetic keystrokes (voice input, OSK, accessibility tools) bypass Interception entirely; the keyboard backend MUST stay user-mode.
- **Don't swap in the `InputInterceptor` NuGet wrapper for Interception.** Its hook loop unconditionally forwards strokes after the callback, which breaks `SuppressInput`. Stay on the direct P/Invoke in [InterceptionNative.cs](src/Mouse2Joy.Input/Native/InterceptionNative.cs).
- **`interception.dll` is vendored** at [src/Mouse2Joy.App/native/x64/interception.dll](src/Mouse2Joy.App/native/x64/interception.dll) and its SHA256 is pinned alongside it. If you replace it, update the `.sha256` file. The kernel driver itself is installed by the user (`install-interception.exe /install` + reboot); don't try to bundle that.
- **Engine capture is always-on; emulation is the toggle.** `StartCapture()` runs for app lifetime; `EnableEmulation()` / `DisableEmulation()` flip between `Off` / `Active` / `SoftMuted`. This is what lets the toggle and panic hotkeys work as a safety net before any profile is active — don't "optimize" by stopping capture when emulation is off.
- **The panic hotkey (`Ctrl+Shift+F12`) is registered via Win32 `RegisterHotKey` on a hidden message-only window in `App`**, deliberately independent of Interception and the engine. It must survive an engine crash. Don't route it through the normal hotkey matcher.
- **Persistence schema is versioned** (`schemaVersion: 1`). Any change to the JSON shape needs a migration, not a silent break.

## UI conventions

- **Tooltips wrap automatically and can be sectioned.** All `ToolTip` content goes through the implicit style + template selector defined in [App.xaml](src/Mouse2Joy.App/App.xaml). Two paths:
  - **Plain string** (`ToolTip="..."` / `.ToolTip = "..."`) — auto-wraps at 320px. Right for short, single-thought tooltips.
  - **`TooltipContent`** (in [src/Mouse2Joy.UI/Tooltips/](src/Mouse2Joy.UI/Tooltips/)) — three optional sections rendered as `Typical` (italic, dim, at the top, closest to the input), then `Description`, then `Advice`. Use this when a tooltip combines "what it does" + "when to change it" + a typical-range hint, so users see the at-a-glance answer first instead of buried at the end of a paragraph.
  - Don't reach for plain `\n\n` paragraph breaks inside a string when the content has a clear Typical / Advice split — use `TooltipContent`. See [TOOLTIP_AUTO_WRAP.md](ai-docs/implementations/TOOLTIP_AUTO_WRAP.md) for the full design.

## Storage

User data lives under `%APPDATA%\Mouse2Joy\`: `profiles\<name>.json` (one per profile), `settings.json`, `logs\mouse2joy-YYYYMMDD.log` (Serilog, 7-day rolling).

## Response preferences

- **Don't feel bad about asking questions.** The user expects and encourages it. It's better to ask and get it right than to guess and have to redo work. Don't apologize for asking - Never stop asking follow-up questions because you think already asked "enough".
- **Prefer multiple rounds of Q&A over one big design dump.** It is more efficient to ask one focused question or a targeted set of questions, get the user's input, and then ask the next question based on that input. This iterative approach leads to better alignment and a more refined design. The user is happy to engage in multiple rounds of discussion to arrive at the best solution.
- **Prefer interactive Questions using the "ask_user" tool** Utilize the tool for a streamlined interaction. allowing the user to accept your recommendations or provide their own input in a structured way. Before invoking the tool, give a write-up with your options and recommendations. If they overflow the tool.