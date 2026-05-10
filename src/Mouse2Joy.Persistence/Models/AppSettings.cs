namespace Mouse2Joy.Persistence.Models;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string? LastProfileName { get; init; }

    public HotkeyBinding? SoftToggleHotkey { get; init; }

    public HotkeyBinding? HardToggleHotkey { get; init; }

    /// <summary>Map of profile name -> hotkey that activates it.</summary>
    public Dictionary<string, HotkeyBinding> ProfileSwitchHotkeys { get; init; } = new();

    public OverlayLayout Overlay { get; init; } = new();

    public bool StartWithWindows { get; init; }

    public bool StartMinimized { get; init; } = false;

    public bool CloseButtonMinimizesToTray { get; init; } = true;
}
