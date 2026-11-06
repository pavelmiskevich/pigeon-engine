namespace Pigeon.Core.Tests;

/// <summary>Настроение: базовая точка, затухание, стресс, метки (ТЗ §7).</summary>
public sealed class MoodModelTests
{
    private const double Step = 0.1;
    private static readonly MoodModel Model = MoodModel.Default;
    private static readonly NeedModel NeedRules = NeedModel.Default;
    private static readonly Personality Average = Personality.Average;

    [Fact]
    public void Baseline_IsSetByPersonality()
    {
        Assert.Equal(new Mood(0.1, 0.4, 0.0), Model.Baseline(Average));
        Assert.Equal(0.25, Model.Baseline(Average with { Calmness = 100 }).Arousal, 1e-12);
        Assert.Equal(0.55, Model.Baseline(Average with { Calmness = 0 }).Arousal, 1e-12);
        Assert.Equal(0.3, Model.Baseline(Average with { Playfulness = 100, Sociability = 100 }).Valence, 1e-12);
        Assert.Equal(-0.1, Model.Baseline(Average with { Playfulness = 0, Sociability = 0 }).Valence, 1e-12);
    }

    [Fact]
    public void ComfortableNeeds_KeepMoodAtBaseline()
    {
        var baseline = Model.Baseline(Average);

        var mood = Run(baseline, Average, new Needs(0.1, 0.2, 0.29), TimeSpan.FromMinutes(30));

        Assert.Equal(baseline.Valence, mood.Valence, 1e-12);
        Assert.Equal(baseline.Arousal, mood.Arousal, 1e-12);
        Assert.Equal(0.0, mood.Stress);
    }

    [Fact]
    public void Impulse_DecaysToBaselineWithTimeConstantTau()
    {
        var baseline = Model.Baseline(Average);
        var nudged = baseline.Nudge(0.5, 0.0);

        var mood = Run(nudged, Average, Needs.None, Model.DecayTime);

        // После τ остаётся около 1/e отклонения.
        Assert.InRange((mood.Valence - baseline.Valence) / 0.5, 0.36, 0.375);
    }

    [Fact]
    public void CalmPigeon_SettlesFasterThanANervousOne()
    {
        var calm = Average with { Calmness = 100 };
        var nervous = Average with { Calmness = 0 };
        var minutes = TimeSpan.FromMinutes(5);

        var calmMood = Run(Model.Baseline(calm).Nudge(0.0, 0.4), calm, Needs.None, minutes);
        var nervousMood = Run(Model.Baseline(nervous).Nudge(0.0, 0.4), nervous, Needs.None, minutes);

        Assert.True(calmMood.Arousal - Model.Baseline(calm).Arousal < nervousMood.Arousal - Model.Baseline(nervous).Arousal);
    }

    [Fact]
    public void TiredPigeon_BecomesSleepy()
    {
        var needs = new Needs(0.9, 0.1, 0.1);

        var mood = Run(Model.Baseline(Average), Average, needs, TimeSpan.FromMinutes(30));

        Assert.True(mood.Valence < Model.Baseline(Average).Valence);
        Assert.True(mood.Arousal < Model.Baseline(Average).Arousal);
        Assert.Equal(MoodLabel.Sleepy, Model.Classify(mood, needs, NeedRules));
    }

    [Fact]
    public void BoredPigeon_BecomesBored()
    {
        var needs = new Needs(0.1, 0.8, 0.1);

        var mood = Run(Model.Baseline(Average), Average, needs, TimeSpan.FromMinutes(30));

        Assert.True(mood.Arousal > Model.Baseline(Average).Arousal);
        Assert.Equal(MoodLabel.Bored, Model.Classify(mood, needs, NeedRules));
    }

    [Fact]
    public void LonelyPigeon_GetsAnxiousAndStressed()
    {
        var needs = new Needs(0.1, 0.1, 1.0);

        var mood = Run(Model.Baseline(Average), Average, needs, TimeSpan.FromHours(2));

        Assert.Equal(-0.4, mood.Valence, 1e-3);
        Assert.Equal(0.7, mood.Arousal, 1e-3);
        Assert.Equal(MoodLabel.Anxious, Model.Classify(mood, needs, NeedRules));
        Assert.InRange(mood.Stress, 0.27, 0.29);

        var calmed = Run(Model.Baseline(Average), Average, Needs.None, TimeSpan.FromHours(1));
        Assert.Equal(0.0, calmed.Stress);
    }

    [Fact]
    public void Absence_ReturnsMoodToBaselineAndClearsStress()
    {
        var baseline = Model.Baseline(Average);
        var upset = new Mood(-0.6, 0.9, 0.5);

        var shortly = Model.CatchUpAbsence(upset, Average, 60);
        var stepwise = Run(upset, Average, Needs.None, TimeSpan.FromSeconds(60));
        var overnight = Model.CatchUpAbsence(upset, Average, 8 * 3_600);

        Assert.Equal(stepwise.Valence, shortly.Valence, 1e-3);
        Assert.Equal(stepwise.Arousal, shortly.Arousal, 1e-3);
        Assert.Equal(baseline.Valence, overnight.Valence, 1e-9);
        Assert.Equal(baseline.Arousal, overnight.Arousal, 1e-9);
        Assert.Equal(0.0, overnight.Stress, 1e-9);
    }

    [Fact]
    public void WelcomeBack_DependsOnPersonality()
    {
        var social = Average with { Sociability = 100, Independence = 0 };
        var aloof = Average with { Sociability = 0, Independence = 100 };
        var mood = Model.Baseline(Average);
        var away = Model.LongAbsence;

        Assert.Equal(mood, Model.WelcomeBack(mood, social, away - TimeSpan.FromMinutes(1)));
        Assert.Equal(mood.Valence + 0.3, Model.WelcomeBack(mood, social, away).Valence, 1e-12);
        Assert.Equal(mood.Valence - 0.3, Model.WelcomeBack(mood, aloof, away).Valence, 1e-12);
        Assert.Equal(mood.Valence, Model.WelcomeBack(mood, Average, away).Valence, 1e-12);
        Assert.Equal(mood.Arousal + 0.2, Model.WelcomeBack(mood, Average, away).Arousal, 1e-12);
    }

    [Theory]
    [InlineData(0.1, 0.4, 0.1, 0.1, 0.1, MoodLabel.Content)]
    [InlineData(0.3, 0.4, 0.1, 0.1, 0.1, MoodLabel.Happy)]
    [InlineData(0.3, 0.6, 0.1, 0.1, 0.1, MoodLabel.Playful)]
    [InlineData(0.3, 0.85, 0.1, 0.1, 0.1, MoodLabel.Excited)]
    [InlineData(-0.5, 0.9, 0.1, 0.1, 0.1, MoodLabel.Startled)]
    [InlineData(-0.3, 0.3, 0.1, 0.1, 0.1, MoodLabel.Grumpy)]
    [InlineData(-0.3, 0.6, 0.1, 0.1, 0.1, MoodLabel.Anxious)]
    [InlineData(0.0, 0.3, 0.6, 0.1, 0.1, MoodLabel.Sleepy)]
    [InlineData(0.0, 0.5, 0.6, 0.1, 0.1, MoodLabel.Content)]
    [InlineData(0.0, 0.5, 0.1, 0.5, 0.1, MoodLabel.Bored)]
    [InlineData(0.0, 0.3, 0.2, 0.1, 0.1, MoodLabel.Content)]
    public void Classify_MapsMoodAndDominantNeedToALabel(
        double valence, double arousal, double fatigue, double boredom, double attention, MoodLabel expected)
    {
        Assert.Equal(expected, Model.Classify(new Mood(valence, arousal, 0.0), new Needs(fatigue, boredom, attention), NeedRules));
    }

    [Fact]
    public void Validate_RejectsValuesOutOfRange()
    {
        Assert.Throws<ArgumentException>(() => new Mood(1.5, 0.5, 0.0).Validate());
        Assert.Throws<ArgumentException>(() => new Mood(0.0, -0.1, 0.0).Validate());
        Assert.Throws<ArgumentException>(() => new Mood(0.0, 0.5, double.NaN).Validate());
        Assert.Equal(new Mood(1.0, 0.0, 0.0), new Mood(0.9, 0.1, 0.0).Nudge(0.5, -0.5));
    }

    private static Mood Run(Mood mood, Personality personality, Needs needs, TimeSpan duration)
    {
        var steps = (long)Math.Round(duration.TotalSeconds / Step);
        for (long i = 0; i < steps; i++)
        {
            mood = Model.Advance(mood, personality, needs, NeedRules, Step);
        }

        return mood;
    }
}
