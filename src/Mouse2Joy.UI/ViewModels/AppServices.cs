using Mouse2Joy.Engine;
using Mouse2Joy.Persistence;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels;

public sealed class AppServices
{
    public required InputEngine Engine { get; init; }
    public required ProfileStore Profiles { get; init; }
    public required SettingsStore Settings { get; init; }
    public required Action<AppSettings> ApplySettings { get; init; }

    /// <summary>
    /// Activate a profile from the UI: set it on the engine and enter soft-mute (pad connected,
    /// input passes through). The user then engages emulation explicitly via the soft toggle
    /// hotkey (or the tray "Soft mute" item). Landing in SoftMuted instead of Active is
    /// deliberate — clicking Activate on a profile with a mouse-axis binding would otherwise
    /// freeze the cursor before the user has a chance to reach the toggle hotkey, locking them
    /// out if they haven't configured one.
    /// </summary>
    public required Action<Profile> ApplyActiveProfile { get; init; }

    /// <summary>
    /// Update the engine's active-profile reference without changing engine mode.
    /// For binding/profile edits — the user explicitly does not want saving a
    /// profile to flip the engine into Active and start blocking mouse input.
    /// Pair with <see cref="DeactivateEngine"/> if the caller wants to drop the
    /// engine to Off as part of the same edit.
    /// </summary>
    public required Action<Profile> RefreshActiveProfile { get; init; }

    /// <summary>Disable emulation (engine to Off, pad disconnected, no suppression).</summary>
    public required Action DeactivateEngine { get; init; }
    public required Action<bool> SetOverlayVisible { get; init; }

    /// <summary>
    /// Re-reads the saved overlay layout and pushes it into the live overlay
    /// (per-monitor windows owned by the App-side coordinator). Called whenever
    /// the user adds, edits, or removes a widget so the running overlay reflects
    /// the change without requiring a hide/show cycle.
    /// </summary>
    public required Action ReloadOverlay { get; init; }
}
