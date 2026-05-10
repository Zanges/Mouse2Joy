using System.ComponentModel;
using System.Runtime.CompilerServices;
using Mouse2Joy.Persistence;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels.Editor;

/// <summary>
/// One row in the chain list. Wraps a <see cref="Modifier"/> record and
/// surfaces its mutable params as observable properties. On every param
/// change the wrapper rebuilds the immutable record and notifies the parent
/// editor so validation / preview / save keep in sync.
/// </summary>
public sealed class ModifierCardViewModel : INotifyPropertyChanged
{
    private Modifier _modifier;
    private bool _selected;

    public ModifierCardViewModel(Modifier modifier)
    {
        _modifier = modifier;
    }

    public Modifier Modifier
    {
        get => _modifier;
        private set
        {
            if (!Equals(_modifier, value))
            {
                _modifier = value;
                OnChanged(nameof(Modifier));
                OnChanged(nameof(DisplayName));
                OnChanged(nameof(Enabled));
                ModifierChanged?.Invoke(this);
            }
        }
    }

    public string DisplayName => ModifierTypes.GetDisplayName(_modifier);

    public bool Enabled
    {
        get => _modifier.Enabled;
        set
        {
            if (_modifier.Enabled != value)
            {
                Modifier = _modifier with { Enabled = value };
            }
        }
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                OnChanged();
            }
        }
    }

    /// <summary>Fired when the underlying record changes (param edit, enable toggle).</summary>
    public event Action<ModifierCardViewModel>? ModifierChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Replace the underlying modifier with a new immutable copy. Used by the param-template bindings.</summary>
    public void Update(Modifier next)
    {
        Modifier = next;
    }

    private void OnChanged([CallerMemberName] string? prop = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}
