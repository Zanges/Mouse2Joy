using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mouse2Joy.Persistence;

public static class JsonOptions
{
    public static JsonSerializerOptions Default { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // DictionaryKeyPolicy intentionally NOT set: dictionary keys are
            // user-controlled identifiers (e.g. profile names) and must
            // round-trip exactly as the user typed them.
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
        return options;
    }
}
