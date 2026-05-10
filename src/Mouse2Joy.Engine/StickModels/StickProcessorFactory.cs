using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.StickModels;

public static class StickProcessorFactory
{
    public static IStickProcessor Create(StickModel? model)
    {
        return model switch
        {
            VelocityStickModel v => new VelocityStickProcessor(v),
            AccumulatorStickModel a => new AccumulatorStickProcessor(a),
            PersistentStickModel p => new PersistentStickProcessor(p),
            null => new VelocityStickProcessor(new VelocityStickModel(DecayPerSecond: 8.0, MaxVelocityCounts: 800.0)),
            _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown stick model")
        };
    }
}
