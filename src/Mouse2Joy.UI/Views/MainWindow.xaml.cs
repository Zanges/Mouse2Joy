using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mouse2Joy.Input;
using Mouse2Joy.Persistence.Models;
using Mouse2Joy.UI.Interop;
using Mouse2Joy.UI.ViewModels;
using Mouse2Joy.VirtualPad;

namespace Mouse2Joy.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<WidgetRowViewModel> _widgetRows = new();
    private readonly ObservableCollection<BindingRowViewModel> _bindingRows = new();

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        Loaded += OnLoaded;
        Closing += OnClosing;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedProfile))
            RebuildBindingRows();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Push current settings into hotkey boxes + overlay UI + settings tab.
        var s = _vm.Settings;
        if (s.SoftToggleHotkey is not null) { SoftHk.CapturedKey = s.SoftToggleHotkey.Key; SoftHk.CapturedModifiers = s.SoftToggleHotkey.Modifiers; }
        if (s.HardToggleHotkey is not null) { HardHk.CapturedKey = s.HardToggleHotkey.Key; HardHk.CapturedModifiers = s.HardToggleHotkey.Modifiers; }
        OverlayEnabledCb.IsChecked = s.Overlay.Enabled;
        RebuildWidgetRows(s.Overlay.Widgets);
        WidgetsTable.ItemsSource = _widgetRows;
        BindingsTable.ItemsSource = _bindingRows;
        RebuildBindingRows();
        StartMinimizedCb.IsChecked = s.StartMinimized;
        CloseToTrayCb.IsChecked = s.CloseButtonMinimizesToTray;
        // TODO: Wire StartWithWindows. Mouse2Joy needs admin (Interception) so a plain
        // HKCU\Software\Microsoft\Windows\CurrentVersion\Run entry would launch the app
        // non-elevated at logon and input capture would silently fail. The proper fix is a
        // Task Scheduler entry registered with "Run with highest privileges" — out of scope
        // for this change. Until then the checkbox is shown disabled so the setting surface
        // is discoverable and the persistence field (AppSettings.StartWithWindows) is left
        // wired but unused.
        StartWithWindowsCb.IsChecked = s.StartWithWindows;
        Recheck();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // If the app is already shutting down (Quit button or tray Quit triggered
        // Application.Current.Shutdown(), which then closes this window via App.OnExit),
        // never cancel — let the window close so shutdown completes.
        if (_isShuttingDown) return;

        if (_vm.Settings.CloseButtonMinimizesToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // User wants X to fully exit. ShutdownMode=OnExplicitShutdown means just letting
        // this window close would leave the app alive in the tray (which would look
        // identical to minimize-to-tray from the user's perspective). Trigger a real
        // shutdown — App.OnExit handles all dispose.
        _isShuttingDown = true;
        Application.Current.Shutdown();
    }

    private bool _isShuttingDown;

    private void OnSaveProfile(object sender, RoutedEventArgs e)
    {
        TrySaveAndReport();
    }

    private void OnAddBinding(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null) return;
        var dlg = new BindingEditorWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        _vm.SelectedProfile.Bindings.Add(dlg.Result);
        RebuildBindingRows();
        if (!TrySaveAndReport())
            RollbackBindingsRefresh(() => _vm.SelectedProfile?.Bindings.Remove(dlg.Result));
    }

    private void OnEditBinding(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null) return;
        if (BindingsTable.SelectedItem is not BindingRowViewModel row) return;
        EditBindingById(row.Id);
    }

    private void OnDeleteBinding(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null) return;
        if (BindingsTable.SelectedItem is not BindingRowViewModel row) return;

        var bindings = _vm.SelectedProfile.Bindings;
        var idx = bindings.FindIndex(b => b.Id == row.Id);
        if (idx < 0) return;
        var sel = bindings[idx];
        bindings.RemoveAt(idx);
        RebuildBindingRows();
        if (!TrySaveAndReport())
            RollbackBindingsRefresh(() => _vm.SelectedProfile?.Bindings.Insert(idx, sel));
    }

    /// <summary>
    /// Duplicate the selected binding: clone with a fresh <see cref="Binding.Id"/>
    /// (all other fields verbatim — no "(copy)" suffix per design), append to the
    /// active profile, persist, then immediately open the editor on the clone so
    /// the user can tweak it. Cancelling the editor leaves the duplicate as-is.
    /// </summary>
    private void OnDuplicateBinding(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProfile is null) return;
        if (BindingsTable.SelectedItem is not BindingRowViewModel row) return;
        var bindings = _vm.SelectedProfile.Bindings;
        var sourceIdx = bindings.FindIndex(b => b.Id == row.Id);
        if (sourceIdx < 0) return;

        var clone = bindings[sourceIdx] with { Id = Guid.NewGuid() };
        bindings.Add(clone);
        RebuildBindingRows();
        if (!TrySaveAndReport())
        {
            RollbackBindingsRefresh(() => _vm.SelectedProfile?.Bindings.RemoveAll(b => b.Id == clone.Id));
            return;
        }
        EditBindingById(clone.Id);
    }

    private void OnBindingsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = BindingsTable.SelectedItem is BindingRowViewModel;
        EditBindingBtn.IsEnabled = hasSelection;
        DeleteBindingBtn.IsEnabled = hasSelection;
        DuplicateBindingBtn.IsEnabled = hasSelection;
    }

    /// <summary>
    /// Click anywhere on a row's background opens the editor for that row. The per-row
    /// On checkbox eats the mouse-up via standard hit-testing so this handler doesn't
    /// fire for it.
    /// </summary>
    private void OnBindingRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not BindingRowViewModel row) return;
        EditBindingById(row.Id);
    }

    private void EditBindingById(Guid id)
    {
        if (_vm.SelectedProfile is null) return;
        var bindings = _vm.SelectedProfile.Bindings;
        var idx = bindings.FindIndex(b => b.Id == id);
        if (idx < 0) return;
        var sel = bindings[idx];

        var dlg = new BindingEditorWindow(sel) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        var replacement = dlg.Result with { Id = sel.Id };
        bindings[idx] = replacement;
        RebuildBindingRows();
        if (!TrySaveAndReport())
            RollbackBindingsRefresh(() => { if (_vm.SelectedProfile is not null) _vm.SelectedProfile.Bindings[idx] = sel; });
    }

    private bool TrySaveAndReport()
    {
        if (_vm.TrySaveSelectedProfile(out var error)) return true;
        MessageBox.Show(this, error ?? "Could not save profile.", "Mouse2Joy",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void RollbackBindingsRefresh(Action revert)
    {
        revert();
        RebuildBindingRows();
    }

    /// <summary>
    /// Rebuild the bindings row VM list from the currently selected profile. Each row
    /// carries pre-formatted Source/Target strings (via <see cref="BindingDisplay"/>)
    /// and an auto-generated label fallback when the underlying binding has no
    /// user-set Label.
    /// </summary>
    private void RebuildBindingRows()
    {
        _bindingRows.Clear();
        var profile = _vm.SelectedProfile;
        if (profile is null) return;

        foreach (var b in profile.Bindings)
            _bindingRows.Add(new BindingRowViewModel(b, OnRowEnabledChanged));
    }

    private void OnRowEnabledChanged(BindingRowViewModel row)
    {
        if (_vm.SelectedProfile is null) return;
        var bindings = _vm.SelectedProfile.Bindings;
        var idx = bindings.FindIndex(b => b.Id == row.Id);
        if (idx < 0) return;
        var prev = bindings[idx];
        bindings[idx] = prev with { Enabled = row.Enabled };
        if (!TrySaveAndReport())
        {
            // Roll back both the model and the row VM so the checkbox visually reverts.
            bindings[idx] = prev;
            row.SetEnabledSilently(prev.Enabled);
        }
    }

    private void OnSaveHotkeys(object sender, RoutedEventArgs e)
    {
        var s = _vm.Settings with
        {
            SoftToggleHotkey = SoftHk.CapturedKey.IsNone ? null : new HotkeyBinding(SoftHk.CapturedKey, SoftHk.CapturedModifiers),
            HardToggleHotkey = HardHk.CapturedKey.IsNone ? null : new HotkeyBinding(HardHk.CapturedKey, HardHk.CapturedModifiers)
        };
        _vm.SaveSettings(s);
    }

    private void OnToggleOverlay(object sender, RoutedEventArgs e)
    {
        // Route through the view-model command so AppServices.SetOverlayVisible runs —
        // that path actually shows/hides the per-monitor overlay windows AND persists.
        var requested = OverlayEnabledCb.IsChecked == true;
        if (requested != _vm.Settings.Overlay.Enabled)
        {
            _vm.ToggleOverlayCommand.Execute(null);
            _vm.ReloadSettings();
        }
    }

    private void OnAddWidget(object sender, RoutedEventArgs e)
    {
        var monitors = MonitorEnumerator.Enumerate();
        var siblings = _vm.Settings.Overlay.Widgets;
        var dlg = new WidgetEditorWindow(existing: null, siblings: siblings, monitors: monitors) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        var current = _vm.Settings.Overlay;
        var newList = current.Widgets.ToList();
        newList.Add(dlg.Result);
        _vm.SaveSettings(_vm.Settings with { Overlay = current with { Widgets = newList } });
        RebuildWidgetRows(newList);
        _vm.ReloadOverlay();
    }

    /// <summary>
    /// Click anywhere on a row's background opens the editor for that row. The
    /// per-row Visible toggle and Edit/Remove buttons mark click events handled
    /// (button click semantics) so this handler doesn't fire for them.
    /// </summary>
    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not WidgetRowViewModel row) return;
        OpenEditorForRow(row);
    }

    private void OnEditWidgetSelected(object sender, RoutedEventArgs e)
    {
        if (WidgetsTable.SelectedItem is not WidgetRowViewModel row) return;
        OpenEditorForRow(row);
    }

    private void OpenEditorForRow(WidgetRowViewModel row)
    {
        var current = _vm.Settings.Overlay.Widgets;
        var existing = current.FirstOrDefault(w => w.Id == row.Id);
        if (existing is null) return;

        var monitors = MonitorEnumerator.Enumerate();
        var siblings = current.Where(w => w.Id != existing.Id).ToList();
        var dlg = new WidgetEditorWindow(existing, siblings, monitors) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        var newList = current.ToList();
        var idx = newList.FindIndex(w => w.Id == existing.Id);
        if (idx < 0) return;
        newList[idx] = dlg.Result;
        _vm.SaveSettings(_vm.Settings with { Overlay = _vm.Settings.Overlay with { Widgets = newList } });
        RebuildWidgetRows(newList);
        _vm.ReloadOverlay();
    }

    private void OnDeleteWidgetSelected(object sender, RoutedEventArgs e)
    {
        if (WidgetsTable.SelectedItem is not WidgetRowViewModel row) return;
        RemoveWidget(row);
    }

    private void RemoveWidget(WidgetRowViewModel row)
    {
        var current = _vm.Settings.Overlay.Widgets.ToList();
        var target = current.FirstOrDefault(w => w.Id == row.Id);
        if (target is null) return;

        var directChildren = current.Where(w => w.ParentId == target.Id).ToList();
        if (directChildren.Count > 0)
        {
            var msg = $"\"{target.Type}\" has {directChildren.Count} child widget(s).\n\n" +
                      "Yes = delete this widget and all its descendants.\n" +
                      "No = detach the children and keep them at their current screen positions.\n" +
                      "Cancel = do nothing.";
            var choice = MessageBox.Show(this, msg, "Remove widget", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return;

            if (choice == MessageBoxResult.Yes)
            {
                var toDelete = CollectSubtree(target.Id, current);
                current = current.Where(w => !toDelete.Contains(w.Id)).ToList();
            }
            else // detach: children become roots at their current on-screen rect
            {
                // Resolve every child's current render top-left and monitor *under
                // the existing layout*, then detach by snapping to TopLeft/TopLeft
                // anchors with the resolved top-left as the offset. Anchor math
                // mirrors OverlayCoordinator's resolver.
                var monitorSizes = MonitorEnumerator.Enumerate()
                    .ToDictionary(m => m.Index, m => m.BoundsDip.Size);
                var resolved = new Dictionary<string, (double X, double Y, int Mon)>();
                foreach (var w in current)
                    ResolveRenderTopLeft(w, current, monitorSizes, resolved);

                for (int i = 0; i < current.Count; i++)
                {
                    if (current[i].ParentId != target.Id) continue;
                    if (!resolved.TryGetValue(current[i].Id, out var r)) continue;
                    current[i] = current[i] with
                    {
                        ParentId = null,
                        AnchorPoint = Anchor.TopLeft,
                        SelfAnchor = Anchor.TopLeft,
                        X = r.X,
                        Y = r.Y,
                        MonitorIndex = r.Mon
                    };
                }
                current = current.Where(w => w.Id != target.Id).ToList();
            }
        }
        else
        {
            current = current.Where(w => w.Id != target.Id).ToList();
        }

        _vm.SaveSettings(_vm.Settings with { Overlay = _vm.Settings.Overlay with { Widgets = current } });
        RebuildWidgetRows(current);
        _vm.ReloadOverlay();
    }

    /// <summary>
    /// Compute the widget's render top-left in monitor-local DIPs and its effective
    /// monitor, mirroring <see cref="Mouse2Joy.UI.Overlay.OverlayCoordinator"/>'s
    /// anchor-aware resolver. Used by the detach-children flow so freed children
    /// can be re-pinned with simple TopLeft/TopLeft anchors at the same screen
    /// position. Memoizes into <paramref name="memo"/> so the caller can resolve
    /// every widget in a single pass.
    /// </summary>
    private static (double X, double Y, int Mon) ResolveRenderTopLeft(
        WidgetConfig w,
        IReadOnlyList<WidgetConfig> all,
        IReadOnlyDictionary<int, Size> monitorSizes,
        Dictionary<string, (double X, double Y, int Mon)> memo)
    {
        if (memo.TryGetValue(w.Id, out var done)) return done;

        // Width/Height live directly on the config now — mirrors OverlayCoordinator.
        var size = new Size(Math.Max(0, w.Width), Math.Max(0, w.Height));
        // Cycle defense: pre-seed so re-entry resolves to a degenerate top-left.
        memo[w.Id] = (0, 0, w.MonitorIndex);

        Rect frame;
        int monIdx;
        var parent = string.IsNullOrEmpty(w.ParentId) ? null : all.FirstOrDefault(p => p.Id == w.ParentId);
        if (parent is not null)
        {
            var pr = ResolveRenderTopLeft(parent, all, monitorSizes, memo);
            var pSize = new Size(Math.Max(0, parent.Width), Math.Max(0, parent.Height));
            frame = new Rect(pr.X, pr.Y, pSize.Width, pSize.Height);
            monIdx = pr.Mon;
        }
        else
        {
            var monSize = monitorSizes.TryGetValue(w.MonitorIndex, out var s) ? s : new Size(1920, 1080);
            frame = new Rect(0, 0, monSize.Width, monSize.Height);
            monIdx = w.MonitorIndex;
        }

        var anchor = AnchorPointOnRect(frame, w.AnchorPoint);
        var selfPt = AnchorPointOnRect(new Rect(0, 0, size.Width, size.Height), w.SelfAnchor);
        var x = anchor.X + w.X - selfPt.X;
        var y = anchor.Y + w.Y - selfPt.Y;

        var result = (x, y, monIdx);
        memo[w.Id] = result;
        return result;
    }

    private static Point AnchorPointOnRect(Rect r, Anchor a) => a switch
    {
        Anchor.TopLeft => new Point(r.Left, r.Top),
        Anchor.Top => new Point(r.Left + r.Width / 2.0, r.Top),
        Anchor.TopRight => new Point(r.Right, r.Top),
        Anchor.Left => new Point(r.Left, r.Top + r.Height / 2.0),
        Anchor.Center => new Point(r.Left + r.Width / 2.0, r.Top + r.Height / 2.0),
        Anchor.Right => new Point(r.Right, r.Top + r.Height / 2.0),
        Anchor.BottomLeft => new Point(r.Left, r.Bottom),
        Anchor.Bottom => new Point(r.Left + r.Width / 2.0, r.Bottom),
        Anchor.BottomRight => new Point(r.Right, r.Bottom),
        _ => new Point(r.Left, r.Top)
    };

    private static HashSet<string> CollectSubtree(string rootId, IReadOnlyList<WidgetConfig> all)
    {
        var byParent = all.Where(w => !string.IsNullOrEmpty(w.ParentId))
            .GroupBy(w => w.ParentId!)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());
        var set = new HashSet<string> { rootId };
        var stack = new Stack<string>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!byParent.TryGetValue(id, out var children)) continue;
            foreach (var c in children) if (set.Add(c)) stack.Push(c);
        }
        return set;
    }

    /// <summary>
    /// Rebuild the flat row VM list from the persisted widget list. Each row carries
    /// a display Name (user-defined when set, auto-numbered "Type #N" fallback when
    /// duplicates exist) and a ParentLabel ("Monitor" when standalone — since the
    /// monitor is the reference frame in that case — otherwise the parent's name).
    /// </summary>
    private void RebuildWidgetRows(IReadOnlyList<WidgetConfig> widgets)
    {
        _widgetRows.Clear();

        var byId = widgets.ToDictionary(w => w.Id);

        foreach (var w in widgets)
        {
            var name = WidgetDisplay.ResolveDisplayName(w, widgets);
            var parentLabel = string.IsNullOrEmpty(w.ParentId) || !byId.TryGetValue(w.ParentId, out var parent)
                ? "Monitor"
                : WidgetDisplay.ResolveDisplayName(parent, widgets);
            _widgetRows.Add(new WidgetRowViewModel(w, name, parentLabel, OnRowVisibleChanged));
        }
    }

    private void OnRowVisibleChanged(WidgetRowViewModel row)
    {
        var current = _vm.Settings.Overlay.Widgets.ToList();
        var idx = current.FindIndex(w => w.Id == row.Id);
        if (idx < 0) return;
        current[idx] = current[idx] with { Visible = row.Visible };
        _vm.SaveSettings(_vm.Settings with { Overlay = _vm.Settings.Overlay with { Widgets = current } });
        _vm.ReloadOverlay();
    }

    /// <summary>
    /// Duplicate the selected widget. The clone takes a fresh <see cref="WidgetConfig.Id"/>
    /// (the record's default Guid generator does this when we omit Id from the <c>with</c>
    /// expression's modifications — but <c>with</c> preserves Id by default, so we must
    /// set it explicitly). All other fields are copied verbatim, including ParentId — a
    /// clone of a child widget is itself a sibling of the original, parented to the same
    /// parent. The Options dictionary needs a shallow copy so the two configs don't share
    /// the same backing dictionary instance.
    /// </summary>
    private void OnDuplicateWidget(object sender, RoutedEventArgs e)
    {
        if (WidgetsTable.SelectedItem is not WidgetRowViewModel row) return;
        var current = _vm.Settings.Overlay.Widgets.ToList();
        var source = current.FirstOrDefault(w => w.Id == row.Id);
        if (source is null) return;

        var clone = source with
        {
            Id = Guid.NewGuid().ToString("N"),
            Options = new Dictionary<string, JsonElement>(source.Options)
        };
        current.Add(clone);
        _vm.SaveSettings(_vm.Settings with { Overlay = _vm.Settings.Overlay with { Widgets = current } });
        RebuildWidgetRows(current);
        _vm.ReloadOverlay();

        // Open the editor on the clone immediately so the user can rename / nudge it.
        // Cancelling the editor leaves the clone as-is — the duplicate is persisted
        // independently of the edit dialog's outcome.
        var cloneRow = _widgetRows.FirstOrDefault(r => r.Id == clone.Id);
        if (cloneRow is not null) OpenEditorForRow(cloneRow);
    }

    private void OnWidgetsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = WidgetsTable.SelectedItem is WidgetRowViewModel;
        EditWidgetBtn.IsEnabled = hasSelection;
        DeleteWidgetBtn.IsEnabled = hasSelection;
        DuplicateWidgetBtn.IsEnabled = hasSelection;
    }

    private void OnRecheck(object sender, RoutedEventArgs e) => Recheck();

    private void Recheck()
    {
        VigemStatusTb.Text = "ViGEmBus: " + ViGEmHealth.Probe();
        var ic = DriverHealth.Probe();
        InterceptionStatusTb.Text = "Interception: " + ic;
        AdminStatusTb.Text = "Admin: " + (DriverHealth.IsAdministrator() ? "yes" : "no");

        var exeDir = AppContext.BaseDirectory;
        ExePathTb.Text = "App folder: " + exeDir;

        var hint = ic switch
        {
            InterceptionStatus.DllNotFound =>
                "interception.dll is missing. Place library\\x64\\interception.dll from the Interception release " +
                "into the app folder shown above (next to Mouse2Joy.exe), then click Re-check.",
            InterceptionStatus.DriverNotInstalled =>
                "The Interception kernel driver is not installed. Open the releases page, run install-interception.exe /install " +
                "from an elevated command prompt, then reboot.",
            InterceptionStatus.AdminRequired =>
                "Mouse2Joy needs to run as administrator to capture input via Interception. Restart the app via right-click → Run as administrator.",
            _ => ""
        };
        InterceptionHintTb.Text = hint;
        InterceptionHintTb.Visibility = string.IsNullOrEmpty(hint) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        // The app folder + Re-check button only matter when the user is being told to drop
        // interception.dll into the app folder. The other unhealthy states (DriverNotInstalled,
        // AdminRequired) require an app restart anyway, so Re-check adds nothing there.
        DllMissingPanel.Visibility = ic == InterceptionStatus.DllNotFound
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private void OnOpenVigem(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("https://github.com/nefarius/ViGEmBus/releases/latest") { UseShellExecute = true });

    private void OnOpenInterception(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("https://github.com/oblitum/Interception/releases/latest") { UseShellExecute = true });

    private void OnOpenAppFolder(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(AppContext.BaseDirectory) { UseShellExecute = true });

    private void OnOpenAppDataFolder(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(Mouse2Joy.Persistence.AppPaths.AppDataRoot) { UseShellExecute = true });

    private void OnToggleStartMinimized(object sender, RoutedEventArgs e)
    {
        var s = _vm.Settings with { StartMinimized = StartMinimizedCb.IsChecked == true };
        _vm.SaveSettings(s);
    }

    private void OnToggleCloseToTray(object sender, RoutedEventArgs e)
    {
        var s = _vm.Settings with { CloseButtonMinimizesToTray = CloseToTrayCb.IsChecked == true };
        _vm.SaveSettings(s);
    }

    private void OnQuitApp(object sender, RoutedEventArgs e)
    {
        _isShuttingDown = true;
        Application.Current.Shutdown();
    }
}
