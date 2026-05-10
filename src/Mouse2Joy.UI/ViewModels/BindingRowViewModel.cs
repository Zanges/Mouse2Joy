using CommunityToolkit.Mvvm.ComponentModel;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels;

/// <summary>
/// One row in the Profiles tab's bindings table. The MainWindow rebuilds rows
/// from <see cref="Profile.Bindings"/> whenever the selected profile or its
/// bindings change. Only <see cref="Enabled"/> is bound two-way; the parent
/// passes a delegate for the persistence write-through.
/// </summary>
public sealed partial class BindingRowViewModel : ObservableObject
{
    private readonly Action<BindingRowViewModel> _onEnabledChanged;
    private bool _suppressEnabledWriteThrough;

    /// <summary>Persisted binding id; used by the parent to look up the source <see cref="Binding"/>.</summary>
    public Guid Id { get; }

    /// <summary>Display text for the Source column (e.g. "Mouse X axis").</summary>
    public string SourceLabel { get; }

    /// <summary>Display text for the Target column (e.g. "Left Stick X").</summary>
    public string TargetLabel { get; }

    /// <summary>
    /// Display text for the Label column. Falls back to "Source → Target" when the
    /// underlying binding has no user-set label; <see cref="IsAutoLabel"/> tells the
    /// template to render it dim/italic in that case.
    /// </summary>
    public string LabelText { get; }

    /// <summary>True when the row is showing an auto-generated label, false when the user named it.</summary>
    public bool IsAutoLabel { get; }

    [ObservableProperty]
    private bool _enabled;

    public BindingRowViewModel(Binding source, Action<BindingRowViewModel> onEnabledChanged)
    {
        _onEnabledChanged = onEnabledChanged;
        Id = source.Id;
        SourceLabel = BindingDisplay.FormatSource(source.Source);
        TargetLabel = BindingDisplay.FormatTarget(source.Target);
        if (string.IsNullOrWhiteSpace(source.Label))
        {
            LabelText = BindingDisplay.FormatAuto(source.Source, source.Target);
            IsAutoLabel = true;
        }
        else
        {
            LabelText = source.Label;
            IsAutoLabel = false;
        }
        _enabled = source.Enabled;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_suppressEnabledWriteThrough) return;
        _onEnabledChanged(this);
    }

    /// <summary>
    /// Update <see cref="Enabled"/> without firing the write-through (used while
    /// rebuilding rows from a freshly-saved profile).
    /// </summary>
    public void SetEnabledSilently(bool value)
    {
        _suppressEnabledWriteThrough = true;
        try { Enabled = value; }
        finally { _suppressEnabledWriteThrough = false; }
    }
}
