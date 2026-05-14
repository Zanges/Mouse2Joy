using System.IO;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;
using Mouse2Joy.Engine;
using Mouse2Joy.Input;
using Mouse2Joy.Persistence;
using Mouse2Joy.Persistence.Models;
using Mouse2Joy.UI.Overlay;
using Mouse2Joy.UI.ViewModels;
using Mouse2Joy.UI.Views;
using Mouse2Joy.VirtualPad;
using Serilog;
using Serilog.Extensions.Logging;

namespace Mouse2Joy.App;

// CA1001: App owns several IDisposable fields but is not itself IDisposable.
// This is correct for a WPF Application -- the framework controls the
// lifecycle and calls OnExit (overridden below) for cleanup. Making App
// itself IDisposable would not be called by WPF and adds no value.
#pragma warning disable CA1001
public partial class App : Application
#pragma warning restore CA1001
{
    private SingleInstanceGuard? _guard;
    private InputEngine? _engine;
    private CompositeInputBackend? _input;
    private InterceptionInputBackend? _mouseBackend;
    private LowLevelKeyboardBackend? _keyboardBackend;
    private ViGEmVirtualPad? _pad;
    private MainWindow? _main;
    private OverlayCoordinator? _overlayCoordinator;
    private TaskbarIcon? _tray;
    private PanicHotkey? _panic;
    private ProfileStore? _profileStore;
    private SettingsStore? _settingsStore;

    protected override void OnStartup(StartupEventArgs e)
    {
        _guard = new SingleInstanceGuard("Mouse2Joy.SingleInstance.{a3c1d2e4-9b87-4f6a-9e1f-c0a8d6f7e8b9}");
        if (!_guard.IsFirstInstance)
        {
            MessageBox.Show("Mouse2Joy is already running.", "Mouse2Joy", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        AppPaths.EnsureDirectories();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(AppPaths.LogsDirectory, "mouse2joy-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true)
            .CreateLogger();
        var loggerFactory = new SerilogLoggerFactory(Log.Logger);

        _profileStore = new ProfileStore();
        _settingsStore = new SettingsStore();

        _pad = new ViGEmVirtualPad(loggerFactory.CreateLogger<ViGEmVirtualPad>());
        // Pay the ViGEmClient first-time init at launch instead of on first Activate —
        // the library has a one-shot quirk that surfaces a spurious "ERROR_SUCCESS"
        // exception on the very first connect of a process. Doing it here keeps that
        // hiccup invisible to the user. If the bus is missing this logs and returns;
        // the Setup tab still surfaces the real status.
        try { _pad.Prewarm(); }
        catch (Exception ex) { Log.Warning(ex, "ViGEm pre-warm raised — continuing"); }
        _mouseBackend = new InterceptionInputBackend(loggerFactory.CreateLogger<InterceptionInputBackend>());
        _keyboardBackend = new LowLevelKeyboardBackend(loggerFactory.CreateLogger<LowLevelKeyboardBackend>());
        // The WH_KEYBOARD_LL hook must be installed on a thread with a Win32
        // message pump. App.OnStartup runs on the WPF UI thread, which qualifies.
        try
        {
            _keyboardBackend.Install();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not install WH_KEYBOARD_LL hook — keyboard hotkeys (incl. voice/OSK) will not work");
        }
        _input = new CompositeInputBackend(_mouseBackend, _keyboardBackend, loggerFactory.CreateLogger<CompositeInputBackend>());
        _engine = new InputEngine(_input, _pad, loggerFactory.CreateLogger<InputEngine>());

        var settings = _settingsStore.Load();
        ApplyHotkeySettings(settings);

        _engine.OnProfileSwitchHotkey += SwitchProfileByName;

        // If a previously-active profile exists, set it (emulation is enabled later when user clicks Activate).
        if (!string.IsNullOrEmpty(settings.LastProfileName))
        {
            var p = _profileStore.Load(settings.LastProfileName);
            if (p is not null)
            {
                _engine.SetActiveProfile(p);
            }
        }

        // Start input capture immediately so hotkeys (soft/hard toggle, profile switch, panic)
        // are live from the moment the app launches — the user's safety net to recover from
        // a stuck or wedged emulation state. If this fails (no admin / driver missing) the UI
        // still opens and the Setup tab will surface the issue.
        try
        {
            _engine.StartCapture();
            Log.Information("Input capture started at launch");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not start input capture at launch — hotkeys will not fire until this is resolved (see Setup tab)");
        }

        var services = new AppServices
        {
            Engine = _engine,
            Profiles = _profileStore,
            Settings = _settingsStore,
            ApplySettings = ApplyHotkeySettings,
            ApplyActiveProfile = p => { _engine.SetActiveProfile(p); EnsureEngineArmed(); },
            RefreshActiveProfile = p => _engine.SetActiveProfile(p),
            DeactivateEngine = () => { try { _engine.DisableEmulation(); } catch (Exception ex) { Log.Error(ex, "DisableEmulation failed"); } },
            SetOverlayVisible = SetOverlayVisible,
            ReloadOverlay = ReloadOverlayLayout
        };
        var vm = new MainViewModel(services);

        _main = new MainWindow(vm);
        _main.Closed += (_, _) => _main = null;

        _overlayCoordinator = new OverlayCoordinator(_engine);
        _overlayCoordinator.Apply(settings.Overlay);
        if (settings.Overlay.Enabled)
        {
            _overlayCoordinator.Show();
        }

        SetupTray();

        // Independent safety hotkey via Win32 RegisterHotKey on a hidden message-only
        // window. Fires even if Interception capture is dead. Fixed combo Ctrl+Shift+F12.
        _panic = new PanicHotkey(() =>
        {
            try { _engine?.Panic(); } catch { /* ignore */ }
            try { Dispatcher.BeginInvoke(() => _tray?.ShowBalloonTip("Mouse2Joy", "Panic hotkey: emulation forced Off (Ctrl+Shift+F12).", BalloonIcon.Warning)); } catch { /* ignore */ }
        });
        if (!_panic.Register())
        {
            Log.Warning("Could not register panic hotkey Ctrl+Shift+F12 (already in use?). Continuing without it.");
        }

        if (!settings.StartMinimized)
        {
            _main.Show();
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _panic?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { _overlayCoordinator?.Dispose(); } catch { }
        try { _main?.Close(); } catch { }
        try { _engine?.Shutdown(); } catch { }
        try { _engine?.Dispose(); } catch { }
        try { _input?.Dispose(); } catch { }
        try { _pad?.Dispose(); } catch { }
        try { Log.CloseAndFlush(); } catch { }
        _guard?.Dispose();
        base.OnExit(e);
    }

    private void SetupTray()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "Mouse2Joy",
        };
        // The default Hardcodet.NotifyIcon.Wpf icon is a placeholder; in production a real .ico would be embedded.
        var menu = new System.Windows.Controls.ContextMenu();
        var openMenu = new System.Windows.Controls.MenuItem { Header = "Open" };
        openMenu.Click += (_, _) => ShowMain();
        var enableMenu = new System.Windows.Controls.MenuItem { Header = "Enable / disable" };
        enableMenu.Click += (_, _) => _engine?.RequestToggle(ToggleAction.Hard);
        var muteMenu = new System.Windows.Controls.MenuItem { Header = "Soft mute (toggle)" };
        muteMenu.Click += (_, _) => _engine?.RequestToggle(ToggleAction.Soft);
        var quitMenu = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitMenu.Click += (_, _) => Shutdown();
        menu.Items.Add(openMenu);
        menu.Items.Add(enableMenu);
        menu.Items.Add(muteMenu);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(quitMenu);
        _tray.ContextMenu = menu;
        _tray.TrayMouseDoubleClick += (_, _) => ShowMain();
    }

    private void ShowMain()
    {
        if (_main is null)
        {
            var settings = _settingsStore!.Load();
            var services = new AppServices
            {
                Engine = _engine!,
                Profiles = _profileStore!,
                Settings = _settingsStore!,
                ApplySettings = ApplyHotkeySettings,
                ApplyActiveProfile = p => { _engine!.SetActiveProfile(p); EnsureEngineArmed(); },
                RefreshActiveProfile = p => _engine!.SetActiveProfile(p),
                DeactivateEngine = () => { try { _engine!.DisableEmulation(); } catch (Exception ex) { Log.Error(ex, "DisableEmulation failed"); } },
                SetOverlayVisible = SetOverlayVisible,
                ReloadOverlay = ReloadOverlayLayout
            };
            _main = new MainWindow(new MainViewModel(services));
            _main.Closed += (_, _) => _main = null;
        }
        _main.Show();
        _main.Activate();
    }

    /// <summary>
    /// Connects the virtual pad and lands the engine in SoftMuted (pad armed, input passes through).
    /// The user engages emulation by hitting the soft toggle hotkey or the tray "Soft mute" item.
    /// Going straight to Active here would freeze the mouse on profiles that bind mouse movement,
    /// stranding users who haven't configured a toggle hotkey yet.
    /// </summary>
    private void EnsureEngineArmed()
    {
        if (_engine is null)
        {
            return;
        }

        try { _engine.EnterSoftMute(); }
        catch (Exception ex)
        {
            Log.Error(ex, "EnterSoftMute failed in EnsureEngineArmed");
            MessageBox.Show("Failed to arm emulation: " + ex.Message + "\n\nMake sure Interception and ViGEmBus are installed and the app runs as administrator.",
                "Mouse2Joy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyHotkeySettings(AppSettings s)
    {
        _engine?.SetHotkeys(new HotkeySettings(s.SoftToggleHotkey, s.HardToggleHotkey, s.ProfileSwitchHotkeys));
    }

    private void SetOverlayVisible(bool visible)
    {
        if (_overlayCoordinator is null)
        {
            return;
        }

        if (visible)
        {
            _overlayCoordinator.Show();
        }
        else
        {
            _overlayCoordinator.Hide();
        }

        var s = _settingsStore!.Load();
        s = s with { Overlay = s.Overlay with { Enabled = visible } };
        _settingsStore.Save(s);
    }

    /// <summary>
    /// Reload the per-monitor overlay windows from currently-persisted settings.
    /// Called after the user adds/edits/removes a widget so the live HUD reflects
    /// the change within one render tick.
    /// </summary>
    private void ReloadOverlayLayout()
    {
        if (_overlayCoordinator is null || _settingsStore is null)
        {
            return;
        }

        try
        {
            var s = _settingsStore.Load();
            _overlayCoordinator.Apply(s.Overlay);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ReloadOverlayLayout failed");
        }
    }

    private void SwitchProfileByName(string name)
    {
        if (_profileStore is null || _engine is null)
        {
            return;
        }

        var p = _profileStore.Load(name);
        if (p is not null)
        {
            _engine.SetActiveProfile(p);
        }
    }
}
