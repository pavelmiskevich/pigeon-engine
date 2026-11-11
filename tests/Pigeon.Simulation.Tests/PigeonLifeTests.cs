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
    public void CompletedAction_EndsTheIntentAtOnce()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        simulation.RunTicks(20);
        var (intent, started) = (simulation.Pigeon.Intent, simulation.Pigeon.IntentStartedTick);
        Assert.True(simulation.Pigeon.IntentEndsTick > simulation.Tick + 1);

        simulation.Submit(new ActionFinished(intent, started, Pigeon.World.Abstractions.ActionOutcome.Completed));
        simulation.RunTicks(1);

        Assert.Equal(Pigeon.AI.DecisionTrigger.Completed, simulation.LastDecision!.Trigger);
        Assert.Equal(simulation.Tick, simulation.Pigeon.IntentStartedTick);
    }

    [Fact]
    public void FailedAction_StartlesALittleAndChoosesSomethingElse()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        simulation.RunTicks(20);
        var (intent, started) = (simulation.Pigeon.Intent, simulation.Pigeon.IntentStartedTick);
        var arousal = simulation.Pigeon.Mood.Arousal;

        simulation.Submit(new ActionFinished(intent, started, Pigeon.World.Abstractions.ActionOutcome.Failed, Pigeon.World.Abstractions.ActionFailure.Unreachable));
        simulation.RunTicks(1);

        Assert.Equal(Pigeon.AI.DecisionTrigger.Failed, simulation.LastDecision!.Trigger);
        Assert.True(simulation.Pigeon.Mood.Arousal > arousal + 0.05);
        Assert.Equal(simulation.Tick, simulation.Pigeon.IntentStartedTick);
    }

    [Fact]
    public void StaleActionResult_IsIgnored()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        simulation.RunTicks(20);
        var before = simulation.Pigeon.ToSnapshot();

        simulation.Submit(new ActionFinished(simulation.Pigeon.Intent, simulation.Pigeon.IntentStartedTick - 1, Pigeon.World.Abstractions.ActionOutcome.Completed));
        simulation.RunTicks(1);

        Assert.Equal(before.IntentStartedTick, simulation.Pigeon.IntentStartedTick);
        Assert.Equal(before.IntentEndsTick, simulation.Pigeon.IntentEndsTick);
    }

    [Fact]
    public void Poke_PleasesASociablePigeonMoreThanAnIndependentOne()
    {
        var sociable = new PigeonSimulation(Seed, Options, Personality.Average with { Sociability = 100, Independence = 0 });
        var loner = new PigeonSimulation(Seed, Options, Personality.Average with { Sociability = 0, Independence = 100 });
        var sociableBefore = sociable.Pigeon.Mood.Valence;
        var lonerBefore = loner.Pigeon.Mood.Valence;

        sociable.Submit(new UserPoked());
        loner.Submit(new UserPoked());
        sociable.RunTicks(1);
        loner.RunTicks(1);

        Assert.True(sociable.Pigeon.Mood.Valence - sociableBefore > 0.15);
        Assert.True(loner.Pigeon.Mood.Valence - lonerBefore < 0.01);
    }

    [Fact]
    public void Pokes_InQuickSuccession_AnnoyThePigeon()
    {
        var simulation = new PigeonSimulation(Seed, Options, Personality.Average);
        var valences = new List<double>();

        for (var i = 0; i < 6; i++)
        {
            var before = simulation.Pigeon.Mood.Valence;
            simulation.Submit(new UserPoked());
            simulation.RunTicks(1);
            valences.Add(simulation.Pigeon.Mood.Valence - before);
        }

        Assert.True(valences[0] > 0.0);
        Assert.True(valences[^1] < 0.0);
        Assert.True(simulation.Pigeon.Mood.Arousal >= 0.7);
    }

    [Fact]
    public void Petting_GivesAttentionAndCalmsWithoutWakingASleepingPigeon()
    {
        var simulation = new PigeonSimulation(Seed, Options, Personality.Average);
        simulation.RunTicks(30_000);
        simulation.Pigeon.StartIntent(IntentKind.Sleep, null, simulation.Tick, simulation.Tick + 6_000);
        var before = simulation.Pigeon.ToSnapshot();

        simulation.Submit(new UserPetted());
        simulation.RunTicks(1);

        Assert.True(simulation.Pigeon.Needs.Attention < before.Needs.Attention - 0.3);
        Assert.True(simulation.Pigeon.Mood.Valence > before.Mood.Valence);
        Assert.True(simulation.Pigeon.Mood.Arousal < before.Mood.Arousal);
        Assert.Equal(IntentKind.Sleep, simulation.Pigeon.Intent);
        Assert.Equal(before.IntentStartedTick, simulation.Pigeon.IntentStartedTick);
    }

    [Fact]
    public void PickingUp_ScaresATimidPigeonMoreAndForcesADecision()
    {
        var timid = new PigeonSimulation(Seed, Options, Personality.Average with { Boldness = 0 });
        var bold = new PigeonSimulation(Seed, Options, Personality.Average with { Boldness = 100 });
        timid.RunTicks(100);
        bold.RunTicks(100);
        var timidBefore = timid.Pigeon.Mood;
        var boldBefore = bold.Pigeon.Mood;

        timid.Submit(new UserPickedUp());
        bold.Submit(new UserPickedUp());
        timid.RunTicks(1);
        bold.RunTicks(1);

        Assert.True(timid.Pigeon.Mood.Arousal - timidBefore.Arousal > 2.5 * (bold.Pigeon.Mood.Arousal - boldBefore.Arousal));
        Assert.True(timid.Pigeon.Mood.Valence - timidBefore.Valence < bold.Pigeon.Mood.Valence - boldBefore.Valence);
        Assert.Equal(Pigeon.AI.DecisionTrigger.Interrupted, timid.LastDecision!.Trigger);
    }

    [Fact]
    public void Dropping_ForcesADecision()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        simulation.RunTicks(100);

        simulation.Submit(new UserDropped());
        simulation.RunTicks(1);

        Assert.Equal(Pigeon.AI.DecisionTrigger.Interrupted, simulation.LastDecision!.Trigger);
        Assert.Equal(101, simulation.Tick);
    }

    [Fact]
    public void CursorApproach_ForcesADecisionUnlessThePigeonSleeps()
    {
        var awake = new PigeonSimulation(Seed, Options);
        var asleep = new PigeonSimulation(Seed, Options);
        awake.RunTicks(100);
        asleep.RunTicks(100);
        asleep.Pigeon.StartIntent(IntentKind.Sleep, null, asleep.Tick, asleep.Tick + 6_000);
        var sleepMood = asleep.Pigeon.Mood;
        var decisions = asleep.LastDecision;

        awake.Submit(new CursorApproached());
        asleep.Submit(new CursorApproached());
        awake.RunTicks(1);
        asleep.RunTicks(1);

        Assert.Equal(Pigeon.AI.DecisionTrigger.Interrupted, awake.LastDecision!.Trigger);
        Assert.Equal(101, awake.Tick);
        Assert.Same(decisions, asleep.LastDecision);
        Assert.Equal(IntentKind.Sleep, asleep.Pigeon.Intent);
        Assert.True(asleep.Pigeon.Mood.Arousal <= sleepMood.Arousal);
        Assert.Single(asleep.Journal, e => e.Event is CursorApproached);
    }

    [Fact]
    public void AbsenceEnded_RejectsNegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AbsenceEnded(TimeSpan.FromSeconds(-1)));
    }
}
