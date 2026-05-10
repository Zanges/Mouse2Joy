using FluentAssertions;
using Mouse2Joy.Engine.StickModels;
using Mouse2Joy.Persistence.Models;

namespace Mouse2Joy.Engine.Tests;

public class VelocityStickProcessorTests
{
    [Fact]
    public void No_input_yields_zero()
    {
        var p = new VelocityStickProcessor(new VelocityStickModel(8.0, 800.0));
        p.Advance(0.01).Should().Be(0.0);
    }

    [Fact]
    public void Mouse_delta_produces_positive_deflection()
    {
        var p = new VelocityStickProcessor(new VelocityStickModel(DecayPerSecond: 1.0, MaxVelocityCounts: 100.0));
        p.AddDelta(50);
        // 50 counts in 0.01s = 5000 counts/sec velocity, target=1 (clamped).
        // Convergence is exponential: deflection = 1 - exp(-1*0.01) ≈ 0.00995.
        var first = p.Advance(0.01);
        first.Should().BeGreaterThan(0.0);
        first.Should().BeLessThan(1.0);
    }

    [Fact]
    public void Sustained_input_converges_to_full_deflection()
    {
        var p = new VelocityStickProcessor(new VelocityStickModel(DecayPerSecond: 50.0, MaxVelocityCounts: 100.0));
        // High decay rate => fast tracking. After many ticks of constant
        // saturating input, deflection should reach near-unity.
        double last = 0;
        for (int i = 0; i < 100; i++)
        {
            p.AddDelta(10);  // 10 counts per 0.01s = 1000 counts/s, target = 10 (clamped to 1).
            last = p.Advance(0.01);
        }
        last.Should().BeApproximately(1.0, 1e-3);
    }

    [Fact]
    public void Decays_toward_zero_when_input_stops()
    {
        var p = new VelocityStickProcessor(new VelocityStickModel(DecayPerSecond: 5.0, MaxVelocityCounts: 100.0));
        p.AddDelta(50);
        p.Advance(0.01);
        var d1 = p.Advance(0.5);   // big dt, no input -> target=0, exp(-5*0.5) = 0.082 weight
        d1.Should().BeLessThan(0.2);
        var d2 = p.Advance(2.0);
        d2.Should().BeLessThan(0.001);
    }

    [Fact]
    public void Sign_follows_delta()
    {
        var p = new VelocityStickProcessor(new VelocityStickModel(DecayPerSecond: 50.0, MaxVelocityCounts: 100.0));
        p.AddDelta(-50);
        var d = p.Advance(0.01);
        d.Should().BeLessThan(0.0);
    }

    [Fact]
    public void Reset_clears_state()
    {
        var p = new VelocityStickProcessor(new VelocityStickModel(50.0, 100.0));
        p.AddDelta(80);
        p.Advance(0.01).Should().NotBe(0.0);   // sanity: input did register
        p.Reset();
        p.Advance(0.01).Should().Be(0.0);
    }
}

public class AccumulatorStickProcessorTests
{
    [Fact]
    public void Accumulates_delta()
    {
        var p = new AccumulatorStickProcessor(new AccumulatorStickModel(SpringPerSecond: 0.0, CountsPerFullDeflection: 100.0));
        p.AddDelta(50);
        p.Advance(0.01).Should().BeApproximately(0.5, 1e-9);
        p.AddDelta(50);
        p.Advance(0.01).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Spring_pulls_back_toward_center()
    {
        var p = new AccumulatorStickProcessor(new AccumulatorStickModel(SpringPerSecond: 5.0, CountsPerFullDeflection: 100.0));
        p.AddDelta(80);
        p.Advance(0.01);
        var first = p.Advance(0.5);     // exp(-5*0.5) ≈ 0.082
        first.Should().BeApproximately(0.8 * Math.Exp(-5.0 * 0.5) * Math.Exp(-5.0 * 0.01), 1e-3);
    }

    [Fact]
    public void Clamps_to_unit_range()
    {
        var p = new AccumulatorStickProcessor(new AccumulatorStickModel(SpringPerSecond: 0.0, CountsPerFullDeflection: 100.0));
        p.AddDelta(500);
        p.Advance(0.01).Should().Be(1.0);
    }

    [Fact]
    public void Negative_delta_produces_negative_deflection()
    {
        var p = new AccumulatorStickProcessor(new AccumulatorStickModel(0.0, 100.0));
        p.AddDelta(-30);
        p.Advance(0.01).Should().BeApproximately(-0.3, 1e-9);
    }
}

public class PersistentStickProcessorTests
{
    [Fact]
    public void AddDelta_then_advance_holds_position_without_input()
    {
        var p = new PersistentStickProcessor(new PersistentStickModel(CountsPerFullDeflection: 100.0));
        p.AddDelta(50);
        p.Advance(0.01).Should().BeApproximately(0.5, 1e-9);
        // Many ticks with no further input — the stick must NOT decay back to center.
        for (int i = 0; i < 1000; i++)
            p.Advance(0.01).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Reverse_delta_decreases_deflection_from_clamped_point()
    {
        var p = new PersistentStickProcessor(new PersistentStickModel(CountsPerFullDeflection: 100.0));
        p.AddDelta(100);
        p.Advance(0.01).Should().BeApproximately(1.0, 1e-9);
        p.AddDelta(-50);
        p.Advance(0.01).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Overshoot_is_discarded()
    {
        // Push far past full deflection. Recovery distance must be measured
        // from the clamped point (1.0), not from how far the mouse went
        // past the edge.
        var p = new PersistentStickProcessor(new PersistentStickModel(CountsPerFullDeflection: 100.0));
        p.AddDelta(1000);
        p.Advance(0.01).Should().BeApproximately(1.0, 1e-9);
        p.AddDelta(-50);
        p.Advance(0.01).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Clamps_to_unit_range()
    {
        var p = new PersistentStickProcessor(new PersistentStickModel(CountsPerFullDeflection: 100.0));
        p.AddDelta(500);
        p.Advance(0.01).Should().Be(1.0);
        p.AddDelta(-5000);
        p.Advance(0.01).Should().Be(-1.0);
    }

    [Fact]
    public void Negative_delta_produces_negative_deflection()
    {
        var p = new PersistentStickProcessor(new PersistentStickModel(CountsPerFullDeflection: 100.0));
        p.AddDelta(-30);
        p.Advance(0.01).Should().BeApproximately(-0.3, 1e-9);
    }

    [Fact]
    public void Reset_clears_state()
    {
        var p = new PersistentStickProcessor(new PersistentStickModel(CountsPerFullDeflection: 100.0));
        p.AddDelta(80);
        p.Advance(0.01).Should().NotBe(0.0);
        p.Reset();
        p.Advance(0.01).Should().Be(0.0);
    }
}
