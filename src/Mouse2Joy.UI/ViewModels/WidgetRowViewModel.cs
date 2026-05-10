using CommunityToolkit.Mvvm.ComponentModel;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.UI.ViewModels;

/// <summary>
/// One row in the Overlay tab's flat widget table. The MainWindow rebuilds rows
/// from <see cref="OverlayLayout.Widgets"/> whenever settings change. Only
/// <see cref="Visible"/> is bound two-way; the parent passes a delegate for the
/// settings write-through.
/// </summary>
public sealed partial class WidgetRowViewModel : ObservableObject
{
    private readonly Action<WidgetRowViewModel> _onVisibleChanged;
    private bool _suppressVisibleWriteThrough;

    /// <summary>Persisted widget id; used by the parent to look up the underlying config.</summary>
    public string Id { get; }

    /// <summary>The widget type (e.g. "LeftStick"). Display only.</summary>
    public string Type { get; }

    /// <summary>
    /// User-defined label if set, otherwise an auto-numbered "#N" hint when there are
    /// multiple widgets of the same type, otherwise empty.
    /// </summary>
    public string Name { get; }

    /// <summary>Display string for the Parent column ("—" when standalone).</summary>
    public string ParentLabel { get; }

    [ObservableProperty]
    private bool _visible;

    public WidgetRowViewModel(WidgetConfig source, string name, string parentLabel, Action<WidgetRowViewModel> onVisibleChanged)
    {
        _onVisibleChanged = onVisibleChanged;
        Id = source.Id;
        Type = source.Type;
        Name = name;
        ParentLabel = parentLabel;
        _visible = source.Visible;
    }

    partial void OnVisibleChanged(bool value)
    {
        if (_suppressVisibleWriteThrough) return;
        _onVisibleChanged(this);
    }

    /// <summary>
    /// Update <see cref="Visible"/> without firing the write-through (used while
    /// rebuilding rows from settings).
    /// </summary>
    public void SetVisibleSilently(bool value)
    {
        _suppressVisibleWriteThrough = true;
        try { Visible = value; }
        finally { _suppressVisibleWriteThrough = false; }
    }
}
