using Pigeon.Common.Randomness;
using Pigeon.Core;

namespace Pigeon.Simulation.Tests;

/// <summary>Снимок и восстановление симуляции (ADR-0017).</summary>
public sealed class SnapshotTests
{
    private const ulong Seed = 0x5EED_0002UL;
    private static readonly SimulationOptions Options = SimulationOptions.Desktop;

    [Theory]
    [InlineData(0, 5_000)]
    [InlineData(1, 1)]
    [InlineData(1_234, 4_321)]
    [InlineData(9_999, 1)]
    public void RestoredSimulation_ContinuesExactlyLikeTheOriginal(int ticksBefore, int ticksAfter)
    {
        var continuous = Run(ticksBefore + ticksAfter);

        var original = Run(ticksBefore);
        var restored = PigeonSimulation.Restore(original.CreateSnapshot());
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());

        RunWithPokes(restored, ticksAfter);

        Assert.Equal(continuous.Tick, restored.Tick);
        Assert.Equal(continuous.JournalLength, restored.JournalLength);
        Assert.Equal(continuous.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void Snapshot_KeepsPendingInputsAndPartialStep()
    {
        var original = new PigeonSimulation(Seed, Options);
        original.Advance(TimeSpan.FromMilliseconds(1_250));
        original.Submit(new UserPoked());

        var restored = PigeonSimulation.Restore(original.CreateSnapshot());
        original.Advance(TimeSpan.FromMilliseconds(80));
        restored.Advance(TimeSpan.FromMilliseconds(80));

        Assert.Equal(13, restored.Tick);
        Assert.Single(restored.Journal);
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void JournalFrom_ReturnsEntriesAfterAGlobalPosition()
    {
        var original = Run(3_000);
        var restored = PigeonSimulation.Restore(original.CreateSnapshot());
        RunWithPokes(restored, 3_000);

        Assert.Equal(original.JournalLength, restored.JournalOffset);
        Assert.Equal(restored.Journal, restored.GetJournalFrom(restored.JournalOffset));
        Assert.Empty(restored.GetJournalFrom(restored.JournalLength));
        Assert.Throws<ArgumentOutOfRangeException>(() => restored.GetJournalFrom(restored.JournalOffset - 1));
    }

    [Fact]
    public void Restore_RejectsUnsupportedFormatAndBrokenState()
    {
        var snapshot = Run(100).CreateSnapshot();

        Assert.Throws<NotSupportedException>(() => PigeonSimulation.Restore(snapshot with { FormatVersion = 99 }));
        Assert.Throws<ArgumentException>(() => PigeonSimulation.Restore(
            snapshot with { Pigeon = snapshot.Pigeon with { Fatigue = 1.5 } }));
        Assert.Throws<ArgumentException>(() => PigeonSimulation.Restore(
            snapshot with { Pigeon = snapshot.Pigeon with { Intent = (IntentKind)42 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => PigeonSimulation.Restore(
            snapshot with { Accumulated = Options.Step }));
        Assert.Throws<ArgumentException>(() => PigeonSimulation.Restore(
            snapshot with { RandomStreams = [.. snapshot.RandomStreams, .. snapshot.RandomStreams] }));
        Assert.Throws<ArgumentException>(() => PigeonSimulation.Restore(
            snapshot with { RandomStreams = [KeyValuePair.Create(PigeonSimulation.DecisionStreamName, new Pcg32State(1, 2))] }));
    }

    private static PigeonSimulation Run(int ticks)
    {
        var simulation = new PigeonSimulation(Seed, Options);
        RunWithPokes(simulation, ticks);
        return simulation;
    }

    /// <summary>Тычок каждые 777 тиков по глобальному номеру — одинаково при любой нарезке прогона.</summary>
    private static void RunWithPokes(PigeonSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            if ((simulation.Tick + 1) % 777 == 0)
            {
                simulation.Submit(new UserPoked());
            }

            simulation.RunTicks(1);
        }
    }
}
