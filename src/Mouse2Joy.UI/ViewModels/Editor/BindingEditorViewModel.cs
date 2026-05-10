using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Mouse2Joy.Persistence;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels.Editor;

/// <summary>
/// Owns the editor's working state. Source / Target / SuppressInput / Label
/// live here, plus the modifier collection. Validation is recomputed on
/// every relevant change so the OK button and inline banner can react.
///
/// Auto-insert: when source or target change, the VM will insert a
/// converter modifier if the chain becomes type-invalid AND the user has
/// not explicitly removed that converter kind in this session.
/// </summary>
public sealed class BindingEditorViewModel : INotifyPropertyChanged
{
    private InputSource _source;
    private OutputTarget _target;
    private string? _label;
    private bool _suppressInput;
    private ModifierCardViewModel? _selectedCard;
    private ValidationResult _validation = ValidationResult.Ok;
    private string? _autoInsertNotice;
    private readonly Guid _bindingId;
    private readonly bool _enabled;
    private readonly HashSet<Type> _userRemoved = new();

    public BindingEditorViewModel(Binding? initial = null)
    {
        if (initial is null)
        {
            _bindingId = Guid.NewGuid();
            _enabled = true;
            _source = new MouseAxisSource(MouseAxis.X);
            _target = new StickAxisTarget(Stick.Left, AxisComponent.X);
            _suppressInput = true;
            Modifiers = new ObservableCollection<ModifierCardViewModel>();
            // Brand-new mouse-axis → stick-axis: insert default StickDynamics.
            EnsureRequiredConverters();
        }
        else
        {
            _bindingId = initial.Id;
            _enabled = initial.Enabled;
            _source = initial.Source;
            _target = initial.Target;
            _label = initial.Label;
            _suppressInput = initial.SuppressInput;
            Modifiers = new ObservableCollection<ModifierCardViewModel>();
            foreach (var m in initial.Modifiers)
                AppendCard(m);
        }

        Modifiers.CollectionChanged += (_, _) => RevalidateAndRefresh();
        Revalidate();
    }

    public ObservableCollection<ModifierCardViewModel> Modifiers { get; }

    public InputSource Source
    {
        get => _source;
        set
        {
            if (!Equals(_source, value))
            {
                var oldType = ModifierTypes.GetSourceOutputType(_source);
                _source = value;
                OnChanged();
                OnChanged(nameof(SuppressDefault));
                OnChanged(nameof(AutoLabel));
                // If source-output type flipped, we need to reconsider converters.
                if (ModifierTypes.GetSourceOutputType(_source) != oldType)
                    EnsureRequiredConverters();
                Revalidate();
            }
        }
    }

    public OutputTarget Target
    {
        get => _target;
        set
        {
            if (!Equals(_target, value))
            {
                var oldType = ModifierTypes.GetTargetInputType(_target);
                _target = value;
                OnChanged();
                OnChanged(nameof(AutoLabel));
                if (ModifierTypes.GetTargetInputType(_target) != oldType)
                    EnsureRequiredConverters();
                Revalidate();
            }
        }
    }

    public string? Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnChanged(); } }
    }

    public bool SuppressInput
    {
        get => _suppressInput;
        set { if (_suppressInput != value) { _suppressInput = value; OnChanged(); } }
    }

    /// <summary>Default for SuppressInput when the user hasn't touched the checkbox: true for mouse-axis, false otherwise.</summary>
    public bool SuppressDefault => _source is MouseAxisSource;

    public string AutoLabel => BindingDisplay.FormatAuto(_source, _target);

    public ModifierCardViewModel? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (_selectedCard != value)
            {
                if (_selectedCard is not null) _selectedCard.Selected = false;
                _selectedCard = value;
                if (_selectedCard is not null) _selectedCard.Selected = true;
                OnChanged();
                OnChanged(nameof(SelectedProxy));
            }
        }
    }

    /// <summary>Proxy exposing two-way-bindable params for the selected modifier kind. Null when no card is selected.</summary>
    public ModifierParamProxy? SelectedProxy => _selectedCard is null ? null : BuildProxy(_selectedCard);

    private static ModifierParamProxy? BuildProxy(ModifierCardViewModel card) => card.Modifier switch
    {
        StickDynamicsModifier => new StickDynamicsProxy(card),
        DigitalToScalarModifier => new DigitalToScalarProxy(card),
        ScalarToDigitalThresholdModifier => new ScalarToDigitalThresholdProxy(card),
        SensitivityModifier => new SensitivityProxy(card),
        InnerDeadzoneModifier => new InnerDeadzoneProxy(card),
        OuterSaturationModifier => new OuterSaturationProxy(card),
        ResponseCurveModifier => new ResponseCurveProxy(card),
        RampUpModifier => new RampUpProxy(card),
        RampDownModifier => new RampDownProxy(card),
        LimiterModifier => new LimiterProxy(card),
        SmoothingModifier => new SmoothingProxy(card),
        AutoFireModifier => new AutoFireProxy(card),
        HoldToActivateModifier => new HoldToActivateProxy(card),
        TapModifier => new TapProxy(card),
        MultiTapModifier => new MultiTapProxy(card),
        WaitForTapResolutionModifier => new WaitForTapResolutionProxy(card),
        InvertModifier => null,  // no params
        ToggleModifier => null,  // no params
        _ => null
    };

    public ValidationResult Validation
    {
        get => _validation;
        private set
        {
            if (!Equals(_validation, value))
            {
                _validation = value;
                OnChanged();
                OnChanged(nameof(IsValid));
                OnChanged(nameof(ValidationMessage));
            }
        }
    }

    public bool IsValid => _validation.IsValid;
    public string? ValidationMessage => _validation.ErrorMessage;

    public string? AutoInsertNotice
    {
        get => _autoInsertNotice;
        private set
        {
            if (_autoInsertNotice != value)
            {
                _autoInsertNotice = value;
                OnChanged();
            }
        }
    }

    public Binding BuildResult()
    {
        var label = string.IsNullOrWhiteSpace(_label) ? null : _label.Trim();
        return new Binding
        {
            Id = _bindingId,
            Source = _source,
            Target = _target,
            Modifiers = Modifiers.Select(c => c.Modifier).ToArray(),
            Enabled = _enabled,
            Label = label,
            SuppressInput = _suppressInput,
        };
    }

    public void AddModifier(ModifierCatalog.Entry entry)
    {
        AppendCard(entry.Create());
        SelectedCard = Modifiers[^1];
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Modifiers.Count) return;
        var removed = Modifiers[index].Modifier;
        _userRemoved.Add(removed.GetType());
        Modifiers.RemoveAt(index);
        AutoInsertNotice = null;
    }

    public void MoveUp(int index)
    {
        if (index <= 0 || index >= Modifiers.Count) return;
        Modifiers.Move(index, index - 1);
    }

    public void MoveDown(int index)
    {
        if (index < 0 || index >= Modifiers.Count - 1) return;
        Modifiers.Move(index, index + 1);
    }

    /// <summary>Replace the modifier at <paramref name="index"/> with a new immutable copy. Used by param edits.</summary>
    public void Replace(int index, Modifier next)
    {
        if (index < 0 || index >= Modifiers.Count) return;
        Modifiers[index].Update(next);
    }

    private void AppendCard(Modifier m)
    {
        var card = new ModifierCardViewModel(m);
        card.ModifierChanged += _ => RevalidateAndRefresh();
        Modifiers.Add(card);
    }

    private void EnsureRequiredConverters()
    {
        var sourceType = ModifierTypes.GetSourceOutputType(_source);
        var targetType = ModifierTypes.GetTargetInputType(_target);

        // Mouse-axis → stick-axis (or trigger): want StickDynamics first.
        if (sourceType == SignalType.Delta && targetType == SignalType.Scalar)
        {
            if (!Modifiers.Any(c => c.Modifier is StickDynamicsModifier)
                && !_userRemoved.Contains(typeof(StickDynamicsModifier)))
            {
                InsertCardAt(0, StickDynamicsModifier.DefaultVelocity);
                AutoInsertNotice = "Auto-inserted Stick Dynamics — mouse axes need an integrator to produce a stick deflection.";
                return;
            }
        }

        // Digital → Scalar target: prepend DigitalToScalar if missing.
        if (sourceType == SignalType.Digital && targetType == SignalType.Scalar)
        {
            if (!Modifiers.Any(c => c.Modifier is DigitalToScalarModifier)
                && !_userRemoved.Contains(typeof(DigitalToScalarModifier)))
            {
                InsertCardAt(0, DigitalToScalarModifier.Default);
                AutoInsertNotice = "Auto-inserted Digital → Scalar — a digital source needs a converter to drive a scalar target.";
                return;
            }
        }

        AutoInsertNotice = null;
    }

    private void InsertCardAt(int index, Modifier m)
    {
        var card = new ModifierCardViewModel(m);
        card.ModifierChanged += _ => RevalidateAndRefresh();
        Modifiers.Insert(index, card);
    }

    private void RevalidateAndRefresh()
    {
        Revalidate();
    }

    private void Revalidate()
    {
        var modifiers = Modifiers.Select(c => c.Modifier).ToArray();
        Validation = ChainValidator.Validate(_source, modifiers, _target);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
