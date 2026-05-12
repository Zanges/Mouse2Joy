namespace Mouse2Joy.Persistence.Models;

public sealed record Profile
{
    public const int CurrentSchemaVersion = 7;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Name { get; init; } = "";

    public int TickRateHz { get; init; } = 250;

    public List<Binding> Bindings { get; init; } = new();
}
