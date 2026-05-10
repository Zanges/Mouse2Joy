using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mouse2Joy.Engine.State;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _svc;

    public ObservableCollection<Profile> Profiles { get; } = new();

    [ObservableProperty]
    private Profile? _selectedProfile;

    [ObservableProperty]
    private string _activeProfileName = "(none)";

    [ObservableProperty]
    private string _engineMode = "Disabled";

    [ObservableProperty]
    private AppSettings _settings = new();

    public MainViewModel(AppServices svc)
    {
        _svc = svc;
        Reload();

        _svc.Engine.ProfileChanged += name => Application.Current?.Dispatcher.BeginInvoke(() => ActiveProfileName = string.IsNullOrEmpty(name) ? "(none)" : name);
        _svc.Engine.ModeChanged += mode => Application.Current?.Dispatcher.BeginInvoke(() => EngineMode = mode.ToString());
    }

    public void Reload()
    {
        Profiles.Clear();
        foreach (var p in _svc.Profiles.LoadAll().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            Profiles.Add(p);
        Settings = _svc.Settings.Load();
        if (!string.IsNullOrEmpty(Settings.LastProfileName))
        {
            var match = Profiles.FirstOrDefault(p => p.Name == Settings.LastProfileName);
            if (match is not null) SelectedProfile = match;
        }
        SelectedProfile ??= Profiles.FirstOrDefault();
        ActiveProfileName = _svc.Engine.ActiveProfile.Name;
        EngineMode = _svc.Engine.Mode.ToString();
    }

    [RelayCommand]
    private void NewProfile()
    {
        var name = UniqueName("New profile");
        var p = new Profile { Name = name };
        _svc.Profiles.Save(p);
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(x => x.Name == name);
    }

    [RelayCommand]
    private void DuplicateProfile()
    {
        if (SelectedProfile is null) return;
        var name = UniqueName(SelectedProfile.Name + " copy");
        var copy = SelectedProfile with { Name = name };
        _svc.Profiles.Save(copy);
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(x => x.Name == name);
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null) return;
        _svc.Profiles.Delete(SelectedProfile.Name);
        Reload();
    }

    [RelayCommand]
    private void ActivateProfile()
    {
        if (SelectedProfile is null) return;
        _svc.ApplyActiveProfile(SelectedProfile);
        Settings = Settings with { LastProfileName = SelectedProfile.Name };
        _svc.Settings.Save(Settings);
        ActiveProfileName = SelectedProfile.Name;
    }

    public bool TrySaveSelectedProfile(out string? error)
    {
        error = null;
        if (SelectedProfile is null) return true;

        var name = SelectedProfile.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Profile name cannot be empty.";
            return false;
        }

        // Detect a rename collision against another loaded profile. Without this, ProfileStore.Save
        // would silently overwrite the colliding file (sanitized filenames collide case-insensitively),
        // merging two profiles into one. Compare by reference so editing the same profile in place
        // does not flag itself.
        var collision = Profiles.FirstOrDefault(p =>
            !ReferenceEquals(p, SelectedProfile) &&
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (collision is not null)
        {
            error = $"A profile named '{name}' already exists.";
            return false;
        }

        try
        {
            _svc.Profiles.Save(SelectedProfile);

            // If we're editing the currently-active profile, deactivate the engine and
            // refresh its profile reference. We deliberately do NOT call ApplyActiveProfile
            // here: re-activating on every edit would force the engine into Active mode
            // (and start blocking mouse movement on a freshly-added mouse-axis binding) the
            // moment the user clicks Save. The user re-enables explicitly via the Activate
            // button or the configured toggle hotkey.
            if (_svc.Engine.ActiveProfile.Name == SelectedProfile.Name)
            {
                _svc.DeactivateEngine();
                _svc.RefreshActiveProfile(SelectedProfile);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        _svc.Settings.Save(settings);
        _svc.ApplySettings(settings);
    }

    /// <summary>
    /// Re-reads settings from disk into <see cref="Settings"/>. Useful after a
    /// command path mutated settings via <see cref="AppServices"/> (e.g.
    /// SetOverlayVisible) so the locally-cached copy reflects the new state.
    /// </summary>
    public void ReloadSettings() => Settings = _svc.Settings.Load();

    /// <summary>Push the saved overlay layout into the live overlay window.</summary>
    public void ReloadOverlay() => _svc.ReloadOverlay();

    [RelayCommand]
    private void Deactivate() => _svc.DeactivateEngine();

    [RelayCommand]
    private void ToggleOverlay() => _svc.SetOverlayVisible(!Settings.Overlay.Enabled);

    private string UniqueName(string baseName)
    {
        var name = baseName;
        var n = 2;
        while (Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} ({n++})";
        return name;
    }
}
