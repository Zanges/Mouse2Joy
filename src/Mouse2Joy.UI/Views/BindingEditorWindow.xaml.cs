using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Mouse2Joy.Persistence.Models;
using Mouse2Joy.UI.Controls;
using Mouse2Joy.UI.ViewModels.Editor;

namespace Mouse2Joy.UI.Views;

/// <summary>
/// Two-pane modifier-chain editor. The Source/Target/Suppress/Label
/// controls live in code-behind for compatibility with the existing
/// KeyCaptureBox + cascading sub-pickers (each kind has its own ComboBox /
/// KeyCaptureBox that show/hide on parent ComboBox change). Everything
/// modifier-related is bound to <see cref="BindingEditorViewModel"/>.
/// </summary>
public partial class BindingEditorWindow : Window
{
    private readonly BindingEditorViewModel _vm;
    private bool _suppressUpdates;

    public Binding? Result { get; private set; }

    public BindingEditorWindow(Binding? initial = null)
    {
        InitializeComponent();
        _vm = new BindingEditorViewModel(initial);
        DataContext = _vm;

        // Populate ButtonCombo with the GamepadButton enum values.
        foreach (var name in Enum.GetNames(typeof(GamepadButton)))
        {
            ButtonCombo.Items.Add(new ComboBoxItem { Content = name });
        }

        // Populate Add Modifier dropdown.
        AddModifierCombo.ItemsSource = ModifierCatalog.AllEntries;
        AddModifierCombo.SelectedIndex = 0;

        // Wire source/target subpickers.
        SourceKindCombo.SelectionChanged += (_, _) => { UpdateSourceVisibility(); CommitSource(); UpdateAutoLabel(); UpdateSuppressDefault(); };
        MouseAxisCombo.SelectionChanged += (_, _) => CommitSource();
        MouseButtonCombo.SelectionChanged += (_, _) => CommitSource();
        MouseScrollCombo.SelectionChanged += (_, _) => CommitSource();
        KeyBox.LostFocus += (_, _) => CommitSource();

        TargetKindCombo.SelectionChanged += (_, _) => { UpdateTargetVisibility(); CommitTarget(); UpdateAutoLabel(); };
        StickCombo.SelectionChanged += (_, _) => CommitTarget();
        StickAxisCombo.SelectionChanged += (_, _) => CommitTarget();
        TriggerCombo.SelectionChanged += (_, _) => CommitTarget();
        ButtonCombo.SelectionChanged += (_, _) => CommitTarget();
        DPadCombo.SelectionChanged += (_, _) => CommitTarget();

        // Label two-way wiring.
        LabelTb.Text = _vm.Label ?? string.Empty;
        LabelTb.TextChanged += (_, _) => { if (!_suppressUpdates) { _vm.Label = LabelTb.Text; } };

        // Suppress two-way wiring.
        SuppressCb.IsChecked = _vm.SuppressInput;
        SuppressCb.Checked += (_, _) => _vm.SuppressInput = true;
        SuppressCb.Unchecked += (_, _) => _vm.SuppressInput = false;

        // Preview wiring (recompute on chain or source/target changes).
        _vm.PropertyChanged += OnVmChanged;
        _vm.Modifiers.CollectionChanged += (_, _) => RefreshPreview();
        // Subscribe to existing cards for modifier param changes.
        foreach (var card in _vm.Modifiers)
        {
            card.ModifierChanged += _ => RefreshPreview();
        }

        _vm.Modifiers.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (ModifierCardViewModel c in e.NewItems)
                {
                    c.ModifierChanged += _ => RefreshPreview();
                }
            }
        };

        // Initial load: populate sub-pickers from current source/target.
        LoadFrom(_vm.Source, _vm.Target);
        UpdateSourceVisibility();
        UpdateTargetVisibility();
        UpdateAutoLabel();
        RefreshPreview();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BindingEditorViewModel.AutoLabel))
        {
            PlaceholderText.SetText(LabelTb, _vm.AutoLabel);
        }
        else if (e.PropertyName is nameof(BindingEditorViewModel.Source)
              or nameof(BindingEditorViewModel.Target))
        {
            RefreshPreview();
        }
        else if (e.PropertyName == nameof(BindingEditorViewModel.SuppressInput))
        {
            if (SuppressCb.IsChecked != _vm.SuppressInput)
            {
                SuppressCb.IsChecked = _vm.SuppressInput;
            }
        }
    }

    private void RefreshPreview()
    {
        Preview.Source = _vm.Source;
        Preview.Target = _vm.Target;
        Preview.Modifiers = _vm.Modifiers.Select(c => c.Modifier).ToArray();
        Preview.InvalidateVisual();
    }

    private void LoadFrom(InputSource src, OutputTarget tgt)
    {
        _suppressUpdates = true;
        try
        {
            switch (src)
            {
                case MouseAxisSource ma:
                    SourceKindCombo.SelectedIndex = 0; MouseAxisCombo.SelectedIndex = (int)ma.Axis; break;
                case MouseButtonSource mb:
                    SourceKindCombo.SelectedIndex = 1; MouseButtonCombo.SelectedIndex = (int)mb.Button; break;
                case MouseScrollSource ms:
                    SourceKindCombo.SelectedIndex = 2; MouseScrollCombo.SelectedIndex = (int)ms.Direction; break;
                case KeySource ks:
                    SourceKindCombo.SelectedIndex = 3; KeyBox.CapturedKey = ks.Key; break;
            }
            switch (tgt)
            {
                case StickAxisTarget sa:
                    TargetKindCombo.SelectedIndex = 0;
                    StickCombo.SelectedIndex = (int)sa.Stick;
                    StickAxisCombo.SelectedIndex = (int)sa.Component;
                    break;
                case TriggerTarget tt:
                    TargetKindCombo.SelectedIndex = 1; TriggerCombo.SelectedIndex = (int)tt.Trigger; break;
                case ButtonTarget bt:
                    TargetKindCombo.SelectedIndex = 2; ButtonCombo.SelectedIndex = (int)bt.Button; break;
                case DPadTarget dp:
                    TargetKindCombo.SelectedIndex = 3; DPadCombo.SelectedIndex = (int)dp.Direction; break;
            }
        }
        finally { _suppressUpdates = false; }
    }

    private void CommitSource()
    {
        if (_suppressUpdates)
        {
            return;
        }

        InputSource src = SourceKindCombo.SelectedIndex switch
        {
            0 => new MouseAxisSource((MouseAxis)Math.Max(0, MouseAxisCombo.SelectedIndex)),
            1 => new MouseButtonSource((MouseButton)Math.Max(0, MouseButtonCombo.SelectedIndex)),
            2 => new MouseScrollSource((ScrollDirection)Math.Max(0, MouseScrollCombo.SelectedIndex)),
            3 => new KeySource(KeyBox.CapturedKey.IsNone ? new VirtualKey(0, false) : KeyBox.CapturedKey),
            _ => _vm.Source,
        };
        _vm.Source = src;
    }

    private void CommitTarget()
    {
        if (_suppressUpdates)
        {
            return;
        }

        OutputTarget tgt = TargetKindCombo.SelectedIndex switch
        {
            0 => new StickAxisTarget((Stick)Math.Max(0, StickCombo.SelectedIndex), (AxisComponent)Math.Max(0, StickAxisCombo.SelectedIndex)),
            1 => new TriggerTarget((Mouse2Joy.Persistence.Models.Trigger)Math.Max(0, TriggerCombo.SelectedIndex)),
            2 => new ButtonTarget((GamepadButton)Math.Max(0, ButtonCombo.SelectedIndex)),
            3 => new DPadTarget((DPadDirection)Math.Max(0, DPadCombo.SelectedIndex)),
            _ => _vm.Target,
        };
        _vm.Target = tgt;
    }

    private void UpdateSourceVisibility()
    {
        MouseAxisCombo.Visibility = SourceKindCombo.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        MouseButtonCombo.Visibility = SourceKindCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        MouseScrollCombo.Visibility = SourceKindCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        KeyBox.Visibility = SourceKindCombo.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTargetVisibility()
    {
        StickPanel.Visibility = TargetKindCombo.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        TriggerCombo.Visibility = TargetKindCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        ButtonCombo.Visibility = TargetKindCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        DPadCombo.Visibility = TargetKindCombo.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateAutoLabel()
    {
        PlaceholderText.SetText(LabelTb, _vm.AutoLabel);
    }

    /// <summary>
    /// Set the Suppress checkbox to the sensible default for the current source kind ONLY
    /// when the user explicitly changes the source kind (mirrors v1 behavior). Pre-existing
    /// bindings keep their saved value because LoadFrom runs before the SelectionChanged
    /// handler subscribes.
    /// </summary>
    private void UpdateSuppressDefault()
    {
        if (_suppressUpdates)
        {
            return;
        }

        SuppressCb.IsChecked = _vm.SuppressDefault;
    }

    private void OnAddModifier(object sender, RoutedEventArgs e)
    {
        if (AddModifierCombo.SelectedItem is ModifierCatalog.Entry entry)
        {
            _vm.AddModifier(entry);
        }
    }

    private void OnMoveUp(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ModifierCardViewModel card)
        {
            _vm.MoveUp(_vm.Modifiers.IndexOf(card));
        }
    }

    private void OnMoveDown(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ModifierCardViewModel card)
        {
            _vm.MoveDown(_vm.Modifiers.IndexOf(card));
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ModifierCardViewModel card)
        {
            _vm.RemoveAt(_vm.Modifiers.IndexOf(card));
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_vm.Source is KeySource ks && ks.Key.IsNone)
        {
            MessageBox.Show(this, "Please press a key in the source field.", "Mouse2Joy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_vm.IsValid)
        {
            MessageBox.Show(this, _vm.ValidationMessage ?? "Chain is invalid.", "Mouse2Joy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = _vm.BuildResult();
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
