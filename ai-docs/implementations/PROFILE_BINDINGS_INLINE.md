# Profile bindings inline + auto-save

## Context

Three related issues with the bindings UI:

1. **Bindings were not persisting reliably.** The standalone Bindings tab's Add / Edit / Delete handlers mutated `SelectedProfile.Bindings` in memory but never called the save path. Persistence only happened when the user navigated back to the Profiles tab and clicked "Save profile" — easy to forget, easy to lose work.
2. **Bindings shouldn't have been a top-level tab.** A binding is always part of a `Profile` (`Profile.Bindings : List<Binding>`). A sibling tab implied the two were independent and was the seam through which the no-save bug crept in.
3. **Saving an active profile auto-activated emulation.** `MainViewModel.SaveSelectedProfile` called `ApplyActiveProfile` for the currently-active profile, which routes through the activation path and forced the engine into Active mode. The user-visible failure: adding a mouse-axis binding to the active profile would instantly start swallowing mouse movement, with no chance to verify settings before activation. Editing should not activate.

Outcome: bindings are now configured inline on the Profiles tab as part of the selected profile. Add / Edit / Delete auto-save. Saving the active profile drops the engine to `Off` and refreshes the engine's profile reference; activation is only ever triggered explicitly by the user.

The broader engine activation lifecycle (Activate button now lands in SoftMuted, new Deactivate button in the status bar) is documented separately in [ACTIVATE_LANDS_IN_SOFTMUTE.md](ACTIVATE_LANDS_IN_SOFTMUTE.md). The cross-cutting touchpoint here is that this change introduced the `RefreshActiveProfile` / `DeactivateEngine` split on `AppServices`, which the bindings save path uses.

## What changed

- Removed the "Bindings" `TabItem` from [MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml).
- Extended the Profiles tab right pane: below the existing Save profile / Activate row it now shows a "Bindings" header, the Add / Edit / Delete buttons, and the bindings DataGrid (the same DataGrid moved verbatim from the deleted tab).
- The bindings click handlers now auto-save via a new `MainViewModel.TrySaveSelectedProfile(out string? error)`. On failure (e.g. duplicate name on Save profile, IO error) the in-memory mutation is rolled back and a MessageBox is shown.
- `MainViewModel.TrySaveSelectedProfile` also detects duplicate-name collisions against other loaded profiles before delegating to `ProfileStore.Save`. Without this guard, two profiles with the same name would silently merge — `ProfileStore.Save` writes by sanitized filename and would just overwrite.
- Saving the active profile now calls `DeactivateEngine` followed by `RefreshActiveProfile` instead of the old `ApplyActiveProfile`, so a mouse-axis binding added to a live profile no longer auto-blocks the cursor.
- Removed the success "Profile saved." MessageBox from the manual Save button — with auto-save firing on every binding change, a confirmation popup is noise.

## Key decisions

- **Inline, not a separate config dialog.** The user's phrasing was "config window for the profile to configure the bindings", but inline keeps the workflow flat: select profile → see and edit its bindings, no extra click. The Profiles tab right pane was sparse and easily fits the bindings DataGrid below the Name / Tick rate fields.
- **Auto-save bindings, manual Save for Name / Tick rate.** The Name TextBox uses `UpdateSourceTrigger=PropertyChanged`, so auto-saving on every keystroke would (a) hammer disk on every character typed and (b) rewrite the file path on every keystroke during a rename — both bad. Bindings are atomic edits via the BindingEditorWindow dialog, so each completed edit is the right unit of save. The manual Save button stays for the two text fields, with a tooltip that explains the split.
- **Duplicate-name detection in the VM, not `ProfileStore`.** The store's job is atomic file writes; "is this name already used by another profile" is a question only the in-memory loaded list can answer. Putting the check in the VM also lets us compare by reference (so the user editing the same profile in place is not flagged against itself).
- **Roll back failed binding edits.** If `TrySaveSelectedProfile` returns false after an Add/Edit/Delete, the in-memory mutation is reverted so the UI matches what's on disk. Without this, the DataGrid would lie about the saved state. Today this only triggers on IO errors (renames don't happen via binding mutations, so duplicate-name can't fire from the binding handlers), but the rollback keeps the invariant honest if more save-failure modes are added later.
- **Saving the active profile drops engine to `Off`, never to SoftMuted.** `SoftMuted` requires the virtual pad to stay connected and is meant for navigating game menus mid-session. After a config change the safer baseline is full `Off` — pad disconnected, no suppression, hotkeys still live so the user can re-enable on demand. This matches startup behavior, so the engine state after a save is identical to a fresh launch.
- **`ProfileStore` and `Profile` model untouched.** Bindings were already a `List<Binding>` field on `Profile` and the JSON serialization roundtrip tests were already passing — the bug was purely missing save calls in the UI layer.

## Files touched

- [src/Mouse2Joy.UI/Views/MainWindow.xaml](../../src/Mouse2Joy.UI/Views/MainWindow.xaml) — removed Bindings tab, extended Profiles tab right pane to host the bindings editor inline.
- [src/Mouse2Joy.UI/Views/MainWindow.xaml.cs](../../src/Mouse2Joy.UI/Views/MainWindow.xaml.cs) — auto-save in `OnAddBinding` / `OnEditBinding` / `OnDeleteBinding` with rollback on failure; shared `TrySaveAndReport` helper; dropped success MessageBox from `OnSaveProfile`.
- [src/Mouse2Joy.UI/ViewModels/MainViewModel.cs](../../src/Mouse2Joy.UI/ViewModels/MainViewModel.cs) — `SaveSelectedProfile()` replaced by `TrySaveSelectedProfile(out string? error)` with duplicate-name guard and IO error capture; saving the active profile now calls `DeactivateEngine` + `RefreshActiveProfile`.

Deliberately unchanged:

- [src/Mouse2Joy.Persistence/Models/Profile.cs](../../src/Mouse2Joy.Persistence/Models/Profile.cs) — already correct (bindings live on the profile).
- [src/Mouse2Joy.Persistence/ProfileStore.cs](../../src/Mouse2Joy.Persistence/ProfileStore.cs) — atomic write semantics fine; collision check intentionally lives at the VM layer.
- [src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml](../../src/Mouse2Joy.UI/Views/BindingEditorWindow.xaml) and `.cs` — per-binding editor dialog unchanged.

See [ACTIVATE_LANDS_IN_SOFTMUTE.md](ACTIVATE_LANDS_IN_SOFTMUTE.md) for the activation-lifecycle changes (the `RefreshActiveProfile` / `DeactivateEngine` actions on `AppServices`, `EnterSoftMute` engine method, status-bar Deactivate button, and Activate semantics).

## Follow-ups

- Consider promoting `TrySaveSelectedProfile`'s rollback discipline into the VM (so the View doesn't have to know how to revert specific mutations). A `MutateBindings(Action<List<Binding>>)` style helper that snapshots, applies, saves, and rolls back on failure would tighten this up; left out for now to keep the diff small.
- The Name TextBox still allows the user to type a duplicate name and only learns about the collision when they click Save. A live validation indicator (red border + adorner) would be a nicer UX — out of scope here.
- The `Mouse2Joy.UI.dll` copy step into `Mouse2Joy.App` fails when the running app holds a file lock. Not introduced by this change, but worth noting: full-solution `dotnet build` requires the user to close the running Mouse2Joy.exe instance first. Building only the UI project (`dotnet build src/Mouse2Joy.UI/Mouse2Joy.UI.csproj`) works regardless.
