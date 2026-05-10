using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Mouse2Joy.Persistence.Models;
using Mouse2Joy.UI.Controls;
using Mouse2Joy.UI.Interop;
using Mouse2Joy.UI.Overlay.Widgets;
using Mouse2Joy.UI.ViewModels;

namespace Mouse2Joy.UI.Views;

/// <summary>
/// Modal Add/Edit dialog for a single overlay widget. The window holds an
/// in-memory staging <see cref="WidgetConfig"/> that mirrors the form state.
/// Save commits and returns the staging config via <see cref="Result"/>;
/// Cancel discards. Type changes convert the staging config in place
/// (preserving Id/Position/Anchor/Parent/Monitor/Visible/Width/Height,
/// resetting Options to the new type's defaults; ensuring source validity;
/// forcing aspect lock for square-only types).
/// </summary>
public partial class WidgetEditorWindow : Window
{
    /// <summary>
    /// The widget categories the user can pick. The Type combobox uses this list
    /// in order: state-displaying categories first (Status / Axis / TwoAxis / Button)
    /// then Utility (MouseActivity / Background / ButtonGrid).
    /// </summary>
    private static readonly string[] WidgetTypes =
    {
        "Status", "EngineStatusIndicator",
        "Axis", "TwoAxis", "Button",
        "MouseActivity", "Background", "ButtonGrid"
    };

    /// <summary>Types whose visual must stay 1:1; the editor locks aspect on and disables the toggle.</summary>
    private static readonly HashSet<string> SquareOnlyTypes = new() { "TwoAxis", "MouseActivity", "EngineStatusIndicator" };

    /// <summary>
    /// Types that auto-size to their content (font + text). The editor hides the
    /// Size section for these and shows the Font section in its place.
    /// </summary>
    private static readonly HashSet<string> AutoSizedTypes = new() { "Status" };

    /// <summary>
    /// Per-category default size when adding a widget or when changing type while
    /// the user hadn't customised W/H. Values match the previous hard-coded base
    /// sizes so widgets render at the same default look as before.
    /// </summary>
    private static (double W, double H) DefaultSizeFor(string type) => type switch
    {
        "Status" => (8, 8),               // unused (auto-sized) but kept positive for the schema's Min=8
        "EngineStatusIndicator" => (20, 20),
        "Axis" => (120, 16),
        "TwoAxis" => (80, 80),
        "Button" => (32, 32),
        "MouseActivity" => (80, 80),
        "Background" => (200, 120),
        "ButtonGrid" => (180, 60),
        _ => (80, 80)
    };

    private readonly bool _isAdd;
    private readonly IReadOnlyList<WidgetConfig> _siblings;
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly HashSet<string> _excludedParents;

    private WidgetConfig _staging;
    private bool _suppressEvents;

    /// <summary>Set on Save; consumed by the caller after <see cref="ShowDialog"/> returns true.</summary>
    public WidgetConfig? Result { get; private set; }

    /// <summary>
    /// </summary>
    /// <param name="existing">
    /// The widget being edited, or null when adding. When non-null, the dialog
    /// excludes the widget itself and its descendants from the Parent picker
    /// (cycle prevention).
    /// </param>
    /// <param name="siblings">
    /// All other widgets currently in the layout (used to populate the Parent picker
    /// and short labels). Should NOT include <paramref name="existing"/>.
    /// </param>
    /// <param name="monitors">Currently-connected monitors.</param>
    public WidgetEditorWindow(WidgetConfig? existing, IReadOnlyList<WidgetConfig> siblings, IReadOnlyList<MonitorInfo> monitors)
    {
        InitializeComponent();
        _isAdd = existing is null;
        _siblings = siblings;
        _monitors = monitors;

        Title = _isAdd ? "Add widget" : "Edit widget";

        // For an existing widget: exclude self and descendants from the Parent picker
        // so the user can't form a cycle. For Add: nothing is excluded.
        _excludedParents = _isAdd ? new HashSet<string>() : ComputeDescendants(existing!.Id, siblings);
        if (existing is not null) _excludedParents.Add(existing.Id);

        if (existing is not null)
        {
            _staging = existing;
        }
        else
        {
            // Start a brand-new widget at the first category's defaults. Aspect
            // lock is forced on for square-only types (and is the default anyway).
            var initialType = WidgetTypes[0];
            var (defW, defH) = DefaultSizeFor(initialType);
            _staging = new WidgetConfig
            {
                Type = initialType,
                Visible = true,
                // X/Y default to 0 so the widget lands flush against its anchor
                // point on the chosen reference frame; the user typically wants
                // anchor + 0 offset and tweaks from there.
                X = 0,
                Y = 0,
                Width = defW,
                Height = defH,
                LockAspect = SquareOnlyTypes.Contains(initialType) || defW == defH,
                Options = DefaultOptionsFor(initialType)
            };
        }

        PopulateTypeCb();
        PopulateMonitorCb();
        PopulateParentCb();
        PopulateAnchorCombos();

        // Wire size-input change events. NumericUpDown.ValueChanged is a CLR event,
        // not a routed XAML event, so it's hooked here rather than in XAML.
        WidthInput.ValueChanged += OnWidthChanged;
        HeightInput.ValueChanged += OnHeightChanged;

        // Capture the staging aspect ratio before the form populates so the lock
        // toggle preserves whatever ratio the widget had on entry.
        RecaptureLockedAspect();

        SyncFormFromStaging();
        BuildOptionsPanel();
        UpdateAutoLabelPlaceholder();
    }

    private void PopulateAnchorCombos()
    {
        // Same nine values populate both combos. Listed in reading order: top row,
        // middle row, bottom row — matches how users visualise a 3×3 anchor grid.
        var anchors = new[]
        {
            new AnchorChoice(Anchor.TopLeft,     "Top-left"),
            new AnchorChoice(Anchor.Top,         "Top"),
            new AnchorChoice(Anchor.TopRight,    "Top-right"),
            new AnchorChoice(Anchor.Left,        "Left"),
            new AnchorChoice(Anchor.Center,      "Center"),
            new AnchorChoice(Anchor.Right,       "Right"),
            new AnchorChoice(Anchor.BottomLeft,  "Bottom-left"),
            new AnchorChoice(Anchor.Bottom,      "Bottom"),
            new AnchorChoice(Anchor.BottomRight, "Bottom-right")
        };
        AnchorPointCb.ItemsSource = anchors.ToList();
        AnchorPointCb.DisplayMemberPath = nameof(AnchorChoice.Label);
        SelfAnchorCb.ItemsSource = anchors.ToList();
        SelfAnchorCb.DisplayMemberPath = nameof(AnchorChoice.Label);
    }

    private void PopulateTypeCb()
    {
        TypeCb.Items.Clear();
        foreach (var t in WidgetTypes) TypeCb.Items.Add(t);
    }

    private void PopulateMonitorCb()
    {
        MonitorCb.Items.Clear();
        foreach (var m in _monitors)
        {
            var label = $"{m.Index}: {m.DeviceName}{(m.IsPrimary ? "  (primary)" : "")}";
            MonitorCb.Items.Add(new MonitorChoice(m.Index, label));
        }
        // If the persisted MonitorIndex isn't in the list (monitor unplugged),
        // append a synthetic entry so we don't drop the user's preference.
        if (_monitors.All(m => m.Index != _staging.MonitorIndex))
        {
            MonitorCb.Items.Add(new MonitorChoice(_staging.MonitorIndex, $"{_staging.MonitorIndex}: (not connected)"));
        }
    }

    private void PopulateParentCb()
    {
        ParentCb.Items.Clear();
        // null id = no parent widget; the monitor is the reference frame for the anchor.
        ParentCb.Items.Add(new ParentChoice(null, "Monitor"));
        // Use the same auto-generated label rule as the widgets table so unnamed
        // widgets appear as "Type" / "Type #N" instead of a 6-char GUID prefix.
        // The label list passed to ResolveDisplayName is the full sibling set so
        // the "#N" disambiguator is consistent with the table.
        foreach (var s in _siblings)
        {
            if (_excludedParents.Contains(s.Id)) continue;
            ParentCb.Items.Add(new ParentChoice(s.Id, WidgetDisplay.ResolveDisplayName(s, _siblings)));
        }
    }

    /// <summary>
    /// Recompute the Label TextBox's placeholder from the current Type. Mirrors
    /// what the widget table would render if the user leaves Label blank: the
    /// bare type name when unique, "Type #N" when a sibling shares the type.
    /// Recomputes against a synthetic <see cref="WidgetConfig"/> with the staged
    /// Id and the freshly-selected Type, so the "#N" math counts the staged
    /// widget itself when its type collides with siblings.
    /// </summary>
    private void UpdateAutoLabelPlaceholder()
    {
        var synthetic = new WidgetConfig { Id = _staging.Id, Type = _staging.Type };
        // Build the list as if the staging widget were inserted alongside its siblings,
        // with the staged Type. ResolveDisplayName walks "all" looking for sameType;
        // we want the "#N" math to include the staged widget if it shares a type.
        var population = new List<WidgetConfig>(_siblings.Count + 1);
        population.AddRange(_siblings);
        population.Add(synthetic);
        var auto = WidgetDisplay.ResolveDisplayName(synthetic, population);
        PlaceholderText.SetText(LabelTb, auto);
    }

    /// <summary>
    /// Build the set of <paramref name="root"/>'s descendant ids in <paramref name="all"/>,
    /// where parenthood is encoded in <see cref="WidgetConfig.ParentId"/>. Used to
    /// keep the Parent picker from offering a cycle.
    /// </summary>
    private static HashSet<string> ComputeDescendants(string root, IReadOnlyList<WidgetConfig> all)
    {
        var byParent = all.Where(w => !string.IsNullOrEmpty(w.ParentId))
            .GroupBy(w => w.ParentId!)
            .ToDictionary(g => g.Key, g => g.Select(w => w.Id).ToList());

        var result = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!byParent.TryGetValue(id, out var children)) continue;
            foreach (var c in children)
            {
                if (result.Add(c)) stack.Push(c);
            }
        }
        return result;
    }

    private static Dictionary<string, JsonElement> DefaultOptionsFor(string type)
    {
        var schema = WidgetSchemas.For(type);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var opt in schema)
        {
            dict[opt.Key] = opt.Default switch
            {
                bool b => JsonSerializer.SerializeToElement(b),
                int i => JsonSerializer.SerializeToElement(i),
                string s => JsonSerializer.SerializeToElement(s),
                _ => JsonSerializer.SerializeToElement(opt.Default)
            };
        }
        return dict;
    }

    private void SyncFormFromStaging()
    {
        _suppressEvents = true;
        try
        {
            TypeCb.SelectedItem = _staging.Type;
            LabelTb.Text = _staging.Name;

            // Find the ParentChoice matching the staged ParentId (null id = "Monitor").
            ParentCb.SelectedItem = ParentCb.Items
                .Cast<ParentChoice>()
                .FirstOrDefault(p => p.Id == _staging.ParentId)
                ?? ParentCb.Items.Cast<ParentChoice>().First();

            MonitorCb.SelectedItem = MonitorCb.Items
                .Cast<MonitorChoice>()
                .FirstOrDefault(m => m.Index == _staging.MonitorIndex)
                ?? MonitorCb.Items.Cast<MonitorChoice>().First();

            VisibleCb.IsChecked = _staging.Visible;

            AnchorPointCb.SelectedItem = ((IEnumerable<AnchorChoice>)AnchorPointCb.ItemsSource)
                .First(a => a.Anchor == _staging.AnchorPoint);
            SelfAnchorCb.SelectedItem = ((IEnumerable<AnchorChoice>)SelfAnchorCb.ItemsSource)
                .First(a => a.Anchor == _staging.SelfAnchor);

            XInput.Value = _staging.X;
            YInput.Value = _staging.Y;
            WidthInput.Value = _staging.Width;
            HeightInput.Value = _staging.Height;
            LockAspectBtn.IsChecked = _staging.LockAspect;
            UpdateMonitorEnabled();
            UpdateLockAspectEnabled();
            UpdateSectionsForType();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    /// <summary>
    /// Square-only widget types (TwoAxis, MouseActivity) force the lock on and
    /// disable the toggle / swap button so the user can't break the visual. The
    /// tooltips surface via <c>ToolTipService.ShowOnDisabled</c> declared in XAML.
    /// </summary>
    private void UpdateLockAspectEnabled()
    {
        var squareOnly = SquareOnlyTypes.Contains(_staging.Type);
        LockAspectBtn.IsEnabled = !squareOnly;
        LockAspectBtn.ToolTip = squareOnly
            ? "This widget must stay square."
            : "Lock aspect ratio";
        SwapWhBtn.IsEnabled = !squareOnly;
        SwapWhBtn.ToolTip = squareOnly
            ? "This widget must stay square."
            : "Swap width and height";
        if (squareOnly && _staging.LockAspect == false)
            _staging = _staging with { LockAspect = true };
    }

    private void UpdateMonitorEnabled()
    {
        var parented = !string.IsNullOrEmpty(_staging.ParentId);
        MonitorCb.IsEnabled = !parented;
        MonitorCb.ToolTip = parented ? "Inherits monitor from parent." : null;
    }

    // ---- Type change: convert in place ---------------------------------------------

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (TypeCb.SelectedItem is not string newType) return;
        if (newType == _staging.Type) return;

        var oldType = _staging.Type;
        var (oldDefW, oldDefH) = DefaultSizeFor(oldType);
        var (newDefW, newDefH) = DefaultSizeFor(newType);

        // If the user hadn't customised W/H (still at the previous type's defaults),
        // re-seed to the new type's defaults so the widget looks right out of the box.
        // Otherwise preserve their values — they may have a reason.
        var keepSize = _staging.Width != oldDefW || _staging.Height != oldDefH;
        var newW = keepSize ? _staging.Width : newDefW;
        var newH = keepSize ? _staging.Height : newDefH;

        // Square-only types force the lock on. If switching INTO a square-only
        // type, snap H to W (which is what the user is likely "thinking of" as
        // the size, since the H field is the second one and may not have been
        // touched yet on this round).
        var squareOnly = SquareOnlyTypes.Contains(newType);
        var lockAspect = squareOnly || _staging.LockAspect;
        if (squareOnly) newH = newW;

        _staging = _staging with
        {
            Type = newType,
            Options = DefaultOptionsFor(newType),
            Width = newW,
            Height = newH,
            LockAspect = lockAspect
        };

        // Rebuild the form sections that depend on Type.
        _suppressEvents = true;
        try
        {
            WidthInput.Value = newW;
            HeightInput.Value = newH;
            LockAspectBtn.IsChecked = lockAspect;
            UpdateLockAspectEnabled();
            UpdateSectionsForType();
        }
        finally { _suppressEvents = false; }
        BuildOptionsPanel();
        UpdateAutoLabelPlaceholder();
    }

    /// <summary>
    /// Show/hide the Size and Font sections based on the staging widget type.
    /// Auto-sized types (Status text widget) hide the Size controls and show the
    /// Font section in their place — same screen real estate. Other types keep
    /// the Size section and the Font section stays hidden.
    /// </summary>
    private void UpdateSectionsForType()
    {
        var autoSized = AutoSizedTypes.Contains(_staging.Type);
        var sizeVis = autoSized ? Visibility.Collapsed : Visibility.Visible;
        var fontVis = autoSized ? Visibility.Visible : Visibility.Collapsed;

        SizeHeader.Visibility = sizeVis;
        WidthLabel.Visibility = sizeVis;
        WidthInput.Visibility = sizeVis;
        LockSwapRow.Visibility = sizeVis;
        HeightLabel.Visibility = sizeVis;
        HeightInput.Visibility = sizeVis;

        FontHeader.Visibility = fontVis;
        FontHost.Visibility = fontVis;

        if (autoSized)
        {
            BuildFontPanel();
        }
        else
        {
            FontHost.Items.Clear();
        }
    }

    // ---- Size: keep W and H linked when LockAspect is on ----------------------------

    /// <summary>
    /// W/H aspect ratio used when the lock is engaged. Initialised from the
    /// staging values when the editor opens; refreshed every time the lock
    /// transitions off→on so the most recent W/H becomes the new ratio.
    /// </summary>
    private double _lockedAspect = 1.0;

    private void OnWidthChanged(NumericUpDown sender, double oldValue, double newValue)
    {
        if (_suppressEvents) return;
        if (newValue <= 0) return;
        _staging = _staging with { Width = newValue };
        if (_staging.LockAspect)
        {
            // Update H to preserve the locked ratio.
            var newH = newValue / Math.Max(0.0001, _lockedAspect);
            _suppressEvents = true;
            try { HeightInput.Value = newH; }
            finally { _suppressEvents = false; }
            _staging = _staging with { Height = newH };
        }
    }

    private void OnHeightChanged(NumericUpDown sender, double oldValue, double newValue)
    {
        if (_suppressEvents) return;
        if (newValue <= 0) return;
        _staging = _staging with { Height = newValue };
        if (_staging.LockAspect)
        {
            var newW = newValue * _lockedAspect;
            _suppressEvents = true;
            try { WidthInput.Value = newW; }
            finally { _suppressEvents = false; }
            _staging = _staging with { Width = newW };
        }
    }

    private void OnLockAspectClicked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        var nowLocked = LockAspectBtn.IsChecked == true;
        // Square-only types: defensive — the toggle is disabled, but if some
        // edge case fires this anyway, snap right back on.
        if (SquareOnlyTypes.Contains(_staging.Type) && !nowLocked)
        {
            _suppressEvents = true;
            try { LockAspectBtn.IsChecked = true; }
            finally { _suppressEvents = false; }
            return;
        }
        if (nowLocked) RecaptureLockedAspect();
        _staging = _staging with { LockAspect = nowLocked };
    }

    private void RecaptureLockedAspect()
    {
        _lockedAspect = (_staging.Width > 0 && _staging.Height > 0)
            ? _staging.Width / _staging.Height
            : 1.0;
    }

    /// <summary>
    /// Swap Width and Height. The "rotate" use case for an Axis bar — pair this
    /// with switching Orientation horizontal↔vertical to flip a 120×16 bar into
    /// a 16×120 one without doing the math by hand. Disabled for square-only
    /// types because the lock would force them back equal anyway.
    /// </summary>
    private void OnSwapWh(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SwapWidthAndHeight();
    }

    private void SwapWidthAndHeight()
    {
        // Square-only types: no-op. The lock would re-enforce equality immediately.
        if (SquareOnlyTypes.Contains(_staging.Type)) return;

        var newW = _staging.Height;
        var newH = _staging.Width;
        _staging = _staging with { Width = newW, Height = newH };
        // Refresh the locked aspect since W and H just changed; otherwise the
        // next typed edit would reshape based on the pre-swap ratio.
        RecaptureLockedAspect();

        _suppressEvents = true;
        try
        {
            WidthInput.Value = newW;
            HeightInput.Value = newH;
        }
        finally { _suppressEvents = false; }
    }

    private void OnResetX(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        // Bypass the change handler so this doesn't get treated as a manual
        // edit (it's a deliberate reset, not the user typing 0).
        _suppressEvents = true;
        try { XInput.Value = 0; }
        finally { _suppressEvents = false; }
        _staging = _staging with { X = 0 };
    }

    private void OnResetY(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _suppressEvents = true;
        try { YInput.Value = 0; }
        finally { _suppressEvents = false; }
        _staging = _staging with { Y = 0 };
    }

    // ---- Parent change: flip absolute<->offset semantics for the form's X/Y ---------

    private void OnParentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ParentCb.SelectedItem is not ParentChoice choice) return;
        if (choice.Id == _staging.ParentId) return;

        // Defensive: would the choice form a cycle? UI excludes them but a stale
        // selection could still slip in if the layout changed under us.
        if (choice.Id is not null && _excludedParents.Contains(choice.Id))
        {
            // Revert silently.
            _suppressEvents = true;
            try { ParentCb.SelectedItem = ParentCb.Items.Cast<ParentChoice>().First(p => p.Id == _staging.ParentId); }
            finally { _suppressEvents = false; }
            return;
        }

        _staging = _staging with { ParentId = choice.Id };
        UpdateMonitorEnabled();
        // Note: switching parents changes which reference frame the offset is
        // applied against, but we deliberately don't auto-translate the offset.
        // The user just told us "this is now relative to that" — they're about
        // to retype it. Auto-translating would surprise them.
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (MonitorCb.SelectedItem is MonitorChoice m) _staging = _staging with { MonitorIndex = m.Index };
    }

    private void OnVisibleClicked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _staging = _staging with { Visible = VisibleCb.IsChecked == true };
    }

    private void OnAnchorPointChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (AnchorPointCb.SelectedItem is AnchorChoice c) _staging = _staging with { AnchorPoint = c.Anchor };
    }

    private void OnSelfAnchorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SelfAnchorCb.SelectedItem is AnchorChoice c) _staging = _staging with { SelfAnchor = c.Anchor };
    }

    // ---- Options panel rendering ----------------------------------------------------

    private void BuildOptionsPanel()
    {
        OptionsHost.Items.Clear();
        // Status uses a bespoke options panel because the source picker has
        // dependent fields (the secondary picker and per-source options vary
        // by sourceKind), which the schema-driven panel can't express.
        if (_staging.Type == "Status")
        {
            OptionsHeader.Visibility = Visibility.Visible;
            var panel = new StackPanel();
            BuildStatusOptionsPanel(panel);
            OptionsHost.Items.Add(panel);
            return;
        }
        var schema = WidgetSchemas.For(_staging.Type);
        if (schema.Count == 0)
        {
            OptionsHeader.Visibility = Visibility.Collapsed;
            return;
        }
        OptionsHeader.Visibility = Visibility.Visible;
        var panelDefault = new StackPanel();
        foreach (var opt in schema) panelDefault.Children.Add(BuildOptionField(opt));
        OptionsHost.Items.Add(panelDefault);
    }

    /// <summary>
    /// Build the bespoke Status options panel: source kind dropdown driving
    /// a secondary picker (button name / axis name) and conditional per-source
    /// fields (pressed/released text for buttons; format/decimals for axes),
    /// plus the optional inline label and the show-background pair.
    /// Font controls are NOT here — they live in the dedicated Font section
    /// rendered by <see cref="BuildFontPanel"/>.
    /// </summary>
    private void BuildStatusOptionsPanel(StackPanel panel)
    {
        var dynamicHost = new StackPanel();

        // Source kind enum.
        var sourceKindGrid = MakeRow("Source");
        var sourceKindCb = new ComboBox();
        foreach (var k in Mouse2Joy.UI.Overlay.Widgets.StatusWidget.SourceKinds) sourceKindCb.Items.Add(k);
        sourceKindCb.SelectedItem = ReadStagingString("sourceKind", "Mode");
        Grid.SetColumn(sourceKindCb, 1);
        sourceKindGrid.Children.Add(sourceKindCb);
        panel.Children.Add(sourceKindGrid);

        // Per-widget label, always visible. For most source kinds this is an
        // optional prefix shown before the resolved value; for sourceKind=Text
        // it IS the rendered content (the resolver returns "" for Text, so the
        // composed string is just the label).
        panel.Children.Add(BuildStringRow("Label", "label", ""));

        // Show-background bool + background color (always present).
        panel.Children.Add(BuildBoolRow("Show background", "showBackground", false));
        panel.Children.Add(BuildColorRow("Background color", "backgroundColor", "#8C000000"));

        // Dynamic (per-source-kind) fields:
        panel.Children.Add(dynamicHost);

        sourceKindCb.SelectionChanged += (_, _) =>
        {
            if (sourceKindCb.SelectedItem is not string sel) return;
            SetStagingOption("sourceKind", JsonSerializer.SerializeToElement(sel));
            BuildStatusDynamicSection(dynamicHost, sel);
        };

        BuildStatusDynamicSection(dynamicHost, ReadStagingString("sourceKind", "Mode"));
    }

    private void BuildStatusDynamicSection(StackPanel host, string sourceKind)
    {
        host.Children.Clear();
        switch (sourceKind)
        {
            case "Text":
            case "Mode":
            case "Profile":
                // No additional fields. Text uses the standalone Label row as
                // its content; Mode and Profile read engine state directly.
                break;
            case "Button":
            {
                var sources = Mouse2Joy.UI.Overlay.Widgets.ButtonWidget.Sources;
                host.Children.Add(BuildEnumRow("Button", "sourceName", sources, sources[0]));
                host.Children.Add(BuildStringRow("Pressed text", "pressedText", "Pressed"));
                host.Children.Add(BuildStringRow("Released text", "releasedText", ""));
                break;
            }
            case "Axis":
            {
                var sources = Mouse2Joy.UI.Overlay.Widgets.AxisWidget.Sources;
                host.Children.Add(BuildEnumRow("Axis", "sourceName", sources, sources[0]));
                host.Children.Add(BuildEnumRow("Format", "axisFormat",
                    Mouse2Joy.UI.Overlay.Widgets.StatusWidget.AxisFormats, "Decimal"));
                host.Children.Add(BuildIntRow("Decimals", "axisDecimals", 2, 0, 4));
                break;
            }
        }
    }

    /// <summary>
    /// Build the Font section for the Status text widget. Family, size,
    /// bold/italic/underline toggles, rotation, vertical-stack, and text color.
    /// Stored alongside the other Status options in the same Options dict.
    /// </summary>
    private void BuildFontPanel()
    {
        FontHost.Items.Clear();
        var panel = new StackPanel();

        // Font family — populated from system font names. Default to Segoe UI.
        var familyGrid = MakeRow("Family");
        var familyCb = new ComboBox();
        foreach (var f in GetSystemFontFamilyNames()) familyCb.Items.Add(f);
        var initialFamily = ReadStagingString("fontFamily", "Segoe UI");
        // Selecting by string works because the items are strings.
        familyCb.SelectedItem = initialFamily;
        if (familyCb.SelectedItem == null && familyCb.Items.Contains(initialFamily))
            familyCb.SelectedItem = initialFamily;
        familyCb.SelectionChanged += (_, _) =>
        {
            if (familyCb.SelectedItem is string sel)
                SetStagingOption("fontFamily", JsonSerializer.SerializeToElement(sel));
        };
        Grid.SetColumn(familyCb, 1);
        familyGrid.Children.Add(familyCb);
        panel.Children.Add(familyGrid);

        // Font size.
        panel.Children.Add(BuildIntRow("Size", "fontSize", 12, 6, 72));

        // Bold / Italic / Underline toggles (one row, three buttons).
        panel.Children.Add(BuildBoldItalicUnderlineRow());

        // Rotation (with reset button).
        panel.Children.Add(BuildRotationRow());

        // Upright letters: when on, each glyph is counter-rotated to stay upright
        // in screen space regardless of the stride angle, so rotation=90 gives
        // top-to-bottom vertical text and rotation=45 gives diagonal text whose
        // letters remain individually readable.
        panel.Children.Add(BuildBoolRow("Upright letters", "verticalStack", false));

        // Letter spacing — extra pixels added between glyphs along the stride axis.
        panel.Children.Add(BuildIntRow("Letter spacing", "letterSpacing", 0, -10, 40));

        // Text color — alpha intentionally disallowed. Translucent text reads
        // poorly on overlay backdrops; opaque is the right default and the
        // user can drop opacity on the whole widget if they want subtlety.
        panel.Children.Add(BuildColorRow("Text color", "textColor", "#FFFFFF", allowAlpha: false));

        FontHost.Items.Add(panel);
    }

    private FrameworkElement BuildBoldItalicUnderlineRow()
    {
        var grid = MakeRow("Style");
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(stack, 1);

        ToggleButton MakeToggle(string content, string key, bool useBold = false, bool useItalic = false, bool useUnderline = false)
        {
            var tb = new ToggleButton
            {
                Content = content,
                Width = 26,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(0),
                FontSize = 12,
                FontWeight = useBold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = useItalic ? FontStyles.Italic : FontStyles.Normal,
                IsChecked = ReadStagingBool(key, false)
            };
            // Match the lock-button highlight style for consistency.
            tb.Style = StyleToggleHighlight;
            if (useUnderline)
            {
                // Show underline visually on the U button only.
                tb.Content = new TextBlock { Text = content, TextDecorations = TextDecorations.Underline };
            }
            tb.Click += (_, _) => SetStagingOption(key, JsonSerializer.SerializeToElement(tb.IsChecked == true));
            return tb;
        }

        stack.Children.Add(MakeToggle("B", "bold", useBold: true));
        stack.Children.Add(MakeToggle("I", "italic", useItalic: true));
        stack.Children.Add(MakeToggle("U", "underline", useUnderline: true));
        grid.Children.Add(stack);
        return grid;
    }

    private FrameworkElement BuildRotationRow()
    {
        var grid = MakeRow("Rotation");
        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var input = new NumericUpDown
        {
            Step = 1,
            Decimals = 0,
            Min = 0,
            Max = 359,
            Value = ReadStagingInt("rotation", 0)
        };
        input.ValueChanged += (_, _, _) =>
        {
            var val = ((int)Math.Round(input.Value) % 360 + 360) % 360;
            SetStagingOption("rotation", JsonSerializer.SerializeToElement(val));
        };
        Grid.SetColumn(input, 0);
        inner.Children.Add(input);

        var reset = new Button
        {
            Content = "⟲",
            Width = 22,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(0),
            FontSize = 11,
            ToolTip = "Reset to 0"
        };
        reset.Click += (_, _) =>
        {
            input.Value = 0;
            SetStagingOption("rotation", JsonSerializer.SerializeToElement(0));
        };
        Grid.SetColumn(reset, 1);
        inner.Children.Add(reset);

        Grid.SetColumn(inner, 1);
        grid.Children.Add(inner);
        return grid;
    }

    // ---- Row builder helpers (shared by Status options + Font panel) ---------------

    private static Grid MakeRow(string label)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);
        return grid;
    }

    private FrameworkElement BuildStringRow(string label, string key, string fallback)
    {
        var grid = MakeRow(label);
        var tb = new TextBox { Text = ReadStagingString(key, fallback) };
        tb.LostFocus += (_, _) => SetStagingOption(key, JsonSerializer.SerializeToElement(tb.Text));
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);
        return grid;
    }

    private FrameworkElement BuildBoolRow(string label, string key, bool fallback)
    {
        var grid = MakeRow(label);
        var cb = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = ReadStagingBool(key, fallback)
        };
        cb.Click += (_, _) => SetStagingOption(key, JsonSerializer.SerializeToElement(cb.IsChecked == true));
        Grid.SetColumn(cb, 1);
        grid.Children.Add(cb);
        return grid;
    }

    private FrameworkElement BuildColorRow(string label, string key, string fallback, bool allowAlpha = true)
    {
        var grid = MakeRow(label);
        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        // When alpha is disallowed, normalise both the displayed and stored
        // values to opaque RGB hex so a stale "#80FFFFFF" from a prior session
        // can't render half-transparent.
        var current = ReadStagingString(key, fallback);
        if (!allowAlpha) current = StripAlpha(current);
        var tb = new TextBox { Text = current };
        var swatch = new Border
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(6, 0, 0, 0),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = TryParseColorBrush(current) ?? Brushes.Transparent
        };
        tb.TextChanged += (_, _) =>
        {
            var raw = tb.Text;
            var brush = TryParseColorBrush(raw);
            if (brush is null) return;
            // For alpha-disallowed fields, only persist after stripping. We
            // don't rewrite the textbox in-place on every keystroke (that
            // would fight the user's typing); the swatch reflects the parsed
            // value and the staged option holds the cleaned hex.
            var stored = allowAlpha ? raw : StripAlpha(raw);
            var storedBrush = allowAlpha ? brush : (TryParseColorBrush(stored) ?? brush);
            swatch.Background = storedBrush;
            SetStagingOption(key, JsonSerializer.SerializeToElement(stored));
        };
        Grid.SetColumn(tb, 0);
        Grid.SetColumn(swatch, 1);
        inner.Children.Add(tb);
        inner.Children.Add(swatch);

        Grid.SetColumn(inner, 1);
        grid.Children.Add(inner);
        return grid;
    }

    /// <summary>
    /// Drop the alpha channel from a hex color string. Accepts <c>#AARRGGBB</c>
    /// and <c>#RRGGBB</c> (returns the input unchanged for the latter); also
    /// accepts hex without the leading <c>#</c>. Returns the input unchanged
    /// if it can't be parsed — letting the caller's normal validation path
    /// surface the issue rather than silently mutating bad input.
    /// </summary>
    private static string StripAlpha(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return hex;
        var trimmed = hex.Trim();
        var hadHash = trimmed.StartsWith('#');
        var body = hadHash ? trimmed[1..] : trimmed;
        if (body.Length == 8)
        {
            return (hadHash ? "#" : "") + body[2..];
        }
        return hex;
    }

    private FrameworkElement BuildIntRow(string label, string key, int fallback, int min, int max)
    {
        var grid = MakeRow(label);
        var input = new NumericUpDown
        {
            Step = 1,
            Decimals = 0,
            Min = min,
            Max = max,
            Value = ReadStagingInt(key, fallback)
        };
        input.ValueChanged += (_, _, _) =>
            SetStagingOption(key, JsonSerializer.SerializeToElement((int)Math.Round(input.Value)));
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        return grid;
    }

    private FrameworkElement BuildEnumRow(string label, string key, string[] values, string fallback)
    {
        var grid = MakeRow(label);
        var cb = new ComboBox();
        foreach (var v in values) cb.Items.Add(v);
        cb.SelectedItem = ReadStagingString(key, fallback);
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is string sel)
                SetStagingOption(key, JsonSerializer.SerializeToElement(sel));
        };
        Grid.SetColumn(cb, 1);
        grid.Children.Add(cb);
        return grid;
    }

    /// <summary>
    /// Programmatically-built highlight style for B/I/U toggles. Matches the
    /// lock-aspect button's :Checked appearance defined in XAML so the "is this
    /// toggle on?" answer is consistent across the editor. Built lazily.
    /// </summary>
    private static Style? _styleToggleHighlight;
    private static Style StyleToggleHighlight
    {
        get
        {
            if (_styleToggleHighlight is not null) return _styleToggleHighlight;
            var s = new Style(typeof(ToggleButton));
            // Inherit from the default ToggleButton style so we don't lose the
            // template's default look.
            s.BasedOn = (Style?)Application.Current?.TryFindResource(typeof(ToggleButton));
            var trigger = new System.Windows.Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(0x80, 0xC8, 0xFF))));
            trigger.Setters.Add(new Setter(Control.BorderBrushProperty,
                new SolidColorBrush(Color.FromRgb(0x30, 0x70, 0xC0))));
            trigger.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
            s.Triggers.Add(trigger);
            _styleToggleHighlight = s;
            return s;
        }
    }

    /// <summary>
    /// Cached list of installed font family display names. Reading the system fonts
    /// is moderately expensive; the list rarely changes during the editor's lifetime.
    /// </summary>
    private static IReadOnlyList<string>? _cachedFontFamilyNames;
    private static IReadOnlyList<string> GetSystemFontFamilyNames()
    {
        if (_cachedFontFamilyNames is not null) return _cachedFontFamilyNames;
        var names = new List<string>();
        foreach (var ff in System.Windows.Media.Fonts.SystemFontFamilies)
        {
            // Source is the canonical "name" (e.g. "Segoe UI"). FamilyNames.Values
            // is a richer multi-locale dictionary but Source is stable.
            if (!string.IsNullOrWhiteSpace(ff.Source)) names.Add(ff.Source);
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        _cachedFontFamilyNames = names;
        return names;
    }

    private FrameworkElement BuildOptionField(OptionDescriptor opt)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = opt.Label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        FrameworkElement editor = opt.Kind switch
        {
            OptionKind.Bool => BuildBoolEditor(opt),
            OptionKind.Color => BuildColorEditor(opt),
            OptionKind.Int => BuildIntEditor(opt),
            OptionKind.String => BuildStringEditor(opt),
            OptionKind.Enum => BuildEnumEditor(opt),
            _ => new TextBlock { Text = "(unsupported)" }
        };
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private FrameworkElement BuildBoolEditor(OptionDescriptor opt)
    {
        var cb = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = ReadStagingBool(opt.Key, (bool)opt.Default)
        };
        cb.Click += (_, _) => SetStagingOption(opt.Key, JsonSerializer.SerializeToElement(cb.IsChecked == true));
        return cb;
    }

    private FrameworkElement BuildColorEditor(OptionDescriptor opt)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var current = ReadStagingString(opt.Key, (string)opt.Default);
        var tb = new TextBox { Text = current };
        var swatch = new Border
        {
            Width = 22,
            Height = 22,
            Margin = new Thickness(6, 0, 0, 0),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = TryParseColorBrush(current) ?? Brushes.Transparent
        };

        tb.TextChanged += (_, _) =>
        {
            var raw = tb.Text;
            var brush = TryParseColorBrush(raw);
            if (brush is null) return;
            swatch.Background = brush;
            SetStagingOption(opt.Key, JsonSerializer.SerializeToElement(raw));
        };

        Grid.SetColumn(tb, 0);
        Grid.SetColumn(swatch, 1);
        grid.Children.Add(tb);
        grid.Children.Add(swatch);
        return grid;
    }

    private FrameworkElement BuildIntEditor(OptionDescriptor opt)
    {
        var current = ReadStagingInt(opt.Key, (int)opt.Default);
        // Build a numeric input + (optional) slider when both Min and Max are specified.
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var input = new NumericUpDown
        {
            Step = 1,
            Decimals = 0,
            Value = current
        };
        // Soft bounds — the input itself doesn't clamp manual entry, but the buttons do.
        if (opt.Min.HasValue) input.Min = opt.Min.Value;
        if (opt.Max.HasValue) input.Max = opt.Max.Value;
        input.SetValue(Grid.ColumnProperty, 0);
        grid.Children.Add(input);

        if (opt.Min.HasValue && opt.Max.HasValue)
        {
            var slider = new Slider
            {
                Minimum = opt.Min.Value,
                Maximum = opt.Max.Value,
                SmallChange = 1,
                LargeChange = Math.Max(1, (opt.Max.Value - opt.Min.Value) / 10),
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Value = Math.Max(opt.Min.Value, Math.Min(opt.Max.Value, current)),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            slider.SetValue(Grid.ColumnProperty, 1);
            grid.Children.Add(slider);

            // Bidirectional: numeric → slider (clamped to slider range) and slider → numeric.
            bool inSync = false;
            input.ValueChanged += (_, _, _) =>
            {
                if (inSync) return;
                inSync = true;
                try { slider.Value = Math.Max(slider.Minimum, Math.Min(slider.Maximum, input.Value)); }
                finally { inSync = false; }
                SetStagingOption(opt.Key, JsonSerializer.SerializeToElement((int)Math.Round(input.Value)));
            };
            slider.ValueChanged += (_, _) =>
            {
                if (inSync) return;
                inSync = true;
                try { input.Value = (int)Math.Round(slider.Value); }
                finally { inSync = false; }
                SetStagingOption(opt.Key, JsonSerializer.SerializeToElement((int)Math.Round(slider.Value)));
            };
        }
        else
        {
            // No slider; commit on every change.
            input.ValueChanged += (_, _, _) =>
                SetStagingOption(opt.Key, JsonSerializer.SerializeToElement((int)Math.Round(input.Value)));
        }

        return grid;
    }

    private FrameworkElement BuildStringEditor(OptionDescriptor opt)
    {
        var tb = new TextBox { Text = ReadStagingString(opt.Key, (string)opt.Default) };
        tb.LostFocus += (_, _) => SetStagingOption(opt.Key, JsonSerializer.SerializeToElement(tb.Text));
        return tb;
    }

    private FrameworkElement BuildEnumEditor(OptionDescriptor opt)
    {
        var cb = new ComboBox();
        if (opt.EnumValues is not null)
            foreach (var v in opt.EnumValues) cb.Items.Add(v);
        var initial = ReadStagingString(opt.Key, (string)opt.Default);
        cb.SelectedItem = initial;

        // Track the previous value so we can react to *changes* on options that
        // have side effects beyond persisting the value (today: "orientation"
        // auto-swaps Width/Height so a horizontal bar becomes a vertical bar
        // without the user doing the math).
        var previous = initial;
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is not string sel) return;
            SetStagingOption(opt.Key, JsonSerializer.SerializeToElement(sel));
            if (opt.Key == "orientation" && sel != previous)
            {
                SwapWidthAndHeight();
            }
            previous = sel;
        };
        return cb;
    }

    // ---- Staging accessors -----------------------------------------------------------

    private void SetStagingOption(string key, JsonElement value)
    {
        var opts = new Dictionary<string, JsonElement>(_staging.Options) { [key] = value };
        _staging = _staging with { Options = opts };
    }

    private bool ReadStagingBool(string key, bool fallback)
    {
        if (!_staging.Options.TryGetValue(key, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private string ReadStagingString(string key, string fallback)
    {
        if (!_staging.Options.TryGetValue(key, out var v)) return fallback;
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    }

    private int ReadStagingInt(string key, int fallback)
    {
        if (!_staging.Options.TryGetValue(key, out var v)) return fallback;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    }

    private static SolidColorBrush? TryParseColorBrush(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is not Color c) return null;
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        catch
        {
            return null;
        }
    }

    // ---- Save / Cancel --------------------------------------------------------------

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Pull final values from inputs that don't already write through on every
        // change (X/Y, Width/Height, Label). The aspect-locked W/H pair is already
        // synced via the change handlers; this just makes a final read consistent.
        _staging = _staging with
        {
            Name = LabelTb.Text?.Trim() ?? "",
            X = XInput.Value,
            Y = YInput.Value,
            Width = WidthInput.Value,
            Height = HeightInput.Value
        };
        Result = _staging;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    // ---- Combobox item types --------------------------------------------------------

    private sealed record ParentChoice(string? Id, string Label);
    private sealed record MonitorChoice(int Index, string Label);
    private sealed record AnchorChoice(Anchor Anchor, string Label);
}
