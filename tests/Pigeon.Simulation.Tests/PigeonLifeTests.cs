using Pigeon.Common.Randomness;
using Pigeon.Core;

namespace Pigeon.Simulation.Tests;

/// <summary>Личность, потребности и настроение внутри симуляции (ТЗ §5–7).</summary>
public sealed class PigeonLifeTests
{
    private const ulong Seed = 0x5EED_0011UL;
    private static readonly SimulationOptions Options = SimulationOptions.Desktop;

    [Fact]
    public void Newborn_GetsPersonalityFromSeedAndMoodAtBaseline()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        var expected = Personality.Generate(new RandomStreams(Seed).Get(PigeonSimulation.PersonalityStreamName));

        Assert.Equal(expected, simulation.Pigeon.Personality);
        Assert.Equal(MoodModel.Default.Baseline(expected), simulation.Pigeon.Mood);
        Assert.Equal(Needs.None, simulation.Pigeon.Needs);
        Assert.NotEqual(simulation.Pigeon.Personality, new PigeonSimulation(Seed + 1, Options).Pigeon.Personality);
    }

    [Fact]
    public void NeedsGrowWhileThePigeonLives()
    {
        var simulation = new PigeonSimulation(Seed, Options);

        simulation.Advance(TimeSpan.FromMinutes(5));

        Assert.True(simulation.Pigeon.Needs.Fatigue > 0.0);
        Assert.True(simulation.Pigeon.Needs.Boredom > 0.0);
        Assert.True(simulation.Pigeon.Needs.Attention > 0.0);
    }

    [Fact]
    public void Poke_GivesAttentionAndANewStimulus()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        simulation.RunTicks(6_000);
        var before = simulation.Pigeon.Needs;
        var arousal = simulation.Pigeon.Mood.Arousal;

        simulation.Submit(new UserPoked());
        simulation.RunTicks(1);

        Assert.True(simulation.Pigeon.Needs.Attention < before.Attention);
        Assert.True(simulation.Pigeon.Needs.Boredom < before.Boredom);
        Assert.True(simulation.Pigeon.Mood.Arousal > arousal);
    }

    [Fact]
    public void Absence_IsCaughtUpWithinWantAndJournaled()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        simulation.RunTicks(3_000);

        simulation.Submit(new AbsenceEnded(TimeSpan.FromDays(2)));
        simulation.RunTicks(1);

        var needs = simulation.Pigeon.Needs;
        Assert.InRange(needs.Fatigue, 0.0, 0.01);
        Assert.InRange(needs.Boredom, 0.69, NeedModel.Default.UrgentAbove + 0.001);
        Assert.InRange(needs.Attention, 0.69, NeedModel.Default.UrgentAbove + 0.001);
        Assert.IsType<AbsenceEnded>(simulation.Journal[^1].Event);
    }

    [Fact]
    public void LongAbsence_ChangesMoodOnReturn()
    {
        var away = new PigeonSimulation(Seed, Options);
        var stayed = new PigeonSimulation(Seed, Options);

        away.Submit(new AbsenceEnded(MoodModel.Default.LongAbsence));
        away.RunTicks(1);
        stayed.Submit(new AbsenceEnded(TimeSpan.FromHours(1)));
        stayed.RunTicks(1);

        Assert.True(away.Pigeon.Mood.Arousal > stayed.Pigeon.Mood.Arousal + 0.15);
    }

    [Fact]
    public void Replay_ReproducesAbsenceAndPokes()
    {
        var live = new PigeonSimulation(Seed, Options);
        live.RunTicks(500);
        live.Submit(new AbsenceEnded(TimeSpan.FromHours(3)));
        live.RunTicks(700);
        live.Submit(new UserPoked());
        live.RunTicks(800);

        var replayed = PigeonSimulation.Replay(Seed, Options, live.Journal, live.Tick);

        Assert.Equal(live.Journal, replayed.Journal);
        Assert.Equal(live.ComputeStateHash(), replayed.ComputeStateHash());
    }

    [Fact]
    public void StateHash_CoversPersonalityNeedsAndMood()
    {
        var snapshot = new PigeonSimulation(Seed, Options).CreateSnapshot();
        var pigeon = snapshot.Pigeon;
        var original = PigeonSimulation.Restore(snapshot).ComputeStateHash();

        PigeonStateSnapshot[] changed =
        [
            pigeon with { Personality = pigeon.Personality with { Boldness = pigeon.Personality.Boldness ^ 1 } },
            pigeon with { Needs = pigeon.Needs with { Attention = 0.5 } },
            pigeon with { Mood = pigeon.Mood with { Stress = 0.5 } },
        ];

        Assert.All(changed, p => Assert.NotEqual(original, PigeonSimulation.Restore(snapshot with { Pigeon = p }).ComputeStateHash()));
    }

    [Fact]
    public void MoodLabel_FollowsTheState()
    {
        var simulation = new PigeonSimulation(Seed, Options);

        Assert.Equal(
            MoodModel.Default.Classify(simulation.Pigeon.Mood, simulation.Pigeon.Needs, NeedModel.Default),
            simulation.MoodLabel);
    }

    [Fact]
    public void LostFooting_StartlesATimidPigeonMoreAndForcesADecision()
    {
        var timid = new PigeonSimulation(Seed, Options, Personality.Average with { Boldness = 0 });
        var bold = new PigeonSimulation(Seed, Options, Personality.Average with { Boldness = 100 });
        timid.RunTicks(100);
        bold.RunTicks(100);
        var timidBefore = timid.Pigeon.Mood.Arousal;
        var boldBefore = bold.Pigeon.Mood.Arousal;

        timid.Submit(new FootingLost(Pigeon.World.Abstractions.DisplacementReason.TerritoryGone));
        bold.Submit(new FootingLost(Pigeon.World.Abstractions.DisplacementReason.TerritoryGone));
        timid.RunTicks(1);
        bold.RunTicks(1);

        Assert.True(timid.Pigeon.Mood.Arousal - timidBefore > 2.5 * (bold.Pigeon.Mood.Arousal - boldBefore));
        Assert.True(timid.Pigeon.Mood.Valence < bold.Pigeon.Mood.Valence);
        Assert.Equal(Pigeon.AI.DecisionTrigger.Interrupted, timid.LastDecision!.Trigger);
        Assert.Equal(101, timid.Tick);
    }

    [Fact]
    public void AbsenceEnded_RejectsNegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AbsenceEnded(TimeSpan.FromSeconds(-1)));
    }
}
