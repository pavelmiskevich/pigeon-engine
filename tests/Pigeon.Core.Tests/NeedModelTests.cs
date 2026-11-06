namespace Pigeon.Core.Tests;

/// <summary>Рост потребностей и досчёт отсутствия (ТЗ §6).</summary>
public sealed class NeedModelTests
{
    private const double Step = 0.1;
    private static readonly NeedModel Model = NeedModel.Default;
    private static readonly Personality Average = Personality.Average;

    [Theory]
    [InlineData(0.0, NeedLevel.Comfort)]
    [InlineData(0.29, NeedLevel.Comfort)]
    [InlineData(0.3, NeedLevel.Want)]
    [InlineData(0.7, NeedLevel.Want)]
    [InlineData(0.71, NeedLevel.Urgent)]
    [InlineData(1.0, NeedLevel.Urgent)]
    public void LevelOf_UsesComfortAndUrgentThresholds(double urgency, NeedLevel expected)
    {
        Assert.Equal(expected, Model.LevelOf(urgency));
    }

    [Fact]
    public void ActivePigeon_GetsTiredInAboutHalfAnHour()
    {
        var needs = Run(Needs.None, Average, resting: false, TimeSpan.FromMinutes(28));

        Assert.Equal(0.7, needs.Fatigue, 1e-6);
        Assert.Equal(NeedLevel.Want, Model.LevelOf(needs.Attention));
        Assert.Equal(NeedLevel.Urgent, Model.LevelOf(needs.Boredom));
    }

    [Fact]
    public void Rest_RecoversFatigueButNotBoredomOrAttention()
    {
        var tired = new Needs(0.8, 0.2, 0.2);

        var rested = Run(tired, Average, resting: true, TimeSpan.FromMinutes(3));

        Assert.Equal(0.3, rested.Fatigue, 1e-6);
        Assert.True(rested.Boredom > tired.Boredom);
        Assert.True(rested.Attention > tired.Attention);
    }

    [Fact]
    public void Urgency_StaysWithinZeroToOne()
    {
        var needs = Run(Needs.None, Average, resting: false, TimeSpan.FromHours(3));
        var rested = Run(needs, Average, resting: true, TimeSpan.FromHours(1));

        Assert.Equal(new Needs(1.0, 1.0, 1.0), needs);
        Assert.Equal(0.0, rested.Fatigue);
    }

    [Fact]
    public void Traits_ChangeHowFastNeedsGrow()
    {
        var minute = TimeSpan.FromMinutes(1);
        var energetic = Run(Needs.None, Average with { Energy = 100 }, resting: false, minute);
        var sluggish = Run(Needs.None, Average with { Energy = 0 }, resting: false, minute);
        var curious = Run(Needs.None, Average with { Curiosity = 100 }, resting: false, minute);
        var social = Run(Needs.None, Average with { Sociability = 100 }, resting: false, minute);
        var independent = Run(Needs.None, Average with { Independence = 100 }, resting: false, minute);
        var average = Run(Needs.None, Average, resting: false, minute);

        Assert.Equal(3.0, sluggish.Fatigue / energetic.Fatigue, 1e-6);
        Assert.Equal(1.5, curious.Boredom / average.Boredom, 1e-6);
        Assert.Equal(1.5, social.Attention / average.Attention, 1e-6);
        Assert.Equal(0.5, independent.Attention / average.Attention, 1e-6);
    }

    [Fact]
    public void Absence_IsCountedAsSleepAndNoNeedRisesAboveWant()
    {
        var before = new Needs(0.9, 0.1, 0.2);

        var after = Model.CatchUpAbsence(before, Average, TimeSpan.FromHours(5));

        Assert.Equal(0.0, after.Fatigue);
        Assert.Equal(Model.UrgentAbove, after.Boredom);
        Assert.Equal(Model.UrgentAbove, after.Attention);
    }

    [Fact]
    public void Absence_DoesNotLowerANeedThatWasAlreadyUrgent()
    {
        var before = new Needs(0.2, 0.95, 0.75);

        var after = Model.CatchUpAbsence(before, Average, TimeSpan.FromHours(1));

        Assert.Equal(before.Boredom, after.Boredom);
        Assert.Equal(before.Attention, after.Attention);
    }

    [Fact]
    public void ShortAbsence_GrowsNeedsLikeLiveTime()
    {
        var away = TimeSpan.FromMinutes(6);

        var after = Model.CatchUpAbsence(Needs.None, Average, away);

        Assert.Equal(0.3, after.Boredom, 1e-9);
        Assert.Equal(0.1, after.Attention, 1e-9);
    }

    [Fact]
    public void Absence_IsCappedAtEightHours()
    {
        var before = new Needs(1.0, 0.0, 0.0);
        var slowRecovery = Model with { FatigueRecoveryPerSecond = 1.0 / (10 * 3_600) };

        var eightHours = slowRecovery.CatchUpAbsence(before, Average, TimeSpan.FromHours(8));
        var threeDays = slowRecovery.CatchUpAbsence(before, Average, TimeSpan.FromDays(3));

        Assert.Equal(8 * 3_600, slowRecovery.AbsenceSeconds(TimeSpan.FromDays(3)));
        Assert.Equal(eightHours, threeDays);
        Assert.Equal(0.2, eightHours.Fatigue, 1e-9);
    }

    [Fact]
    public void NegativeTime_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Model.Advance(Needs.None, Average, resting: false, -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Model.CatchUpAbsence(Needs.None, Average, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Dominant_IsTheMostUrgentNeed_FirstOnTies()
    {
        Assert.Equal(NeedKind.Boredom, new Needs(0.2, 0.6, 0.5).Dominant);
        Assert.Equal(NeedKind.Attention, new Needs(0.2, 0.6, 0.7).Dominant);
        Assert.Equal(NeedKind.Fatigue, Needs.None.Dominant);
        Assert.Equal(NeedKind.Boredom, new Needs(0.1, 0.4, 0.4).Dominant);
    }

    [Fact]
    public void Validate_RejectsUrgencyOutOfRange()
    {
        Assert.Throws<ArgumentException>(() => new Needs(0.0, 1.1, 0.0).Validate());
        Assert.Throws<ArgumentException>(() => new Needs(0.0, 0.0, double.NaN).Validate());
    }

    private static Needs Run(Needs needs, Personality personality, bool resting, TimeSpan duration)
    {
        var steps = (long)Math.Round(duration.TotalSeconds / Step);
        for (long i = 0; i < steps; i++)
        {
            needs = Model.Advance(needs, personality, resting, Step);
        }

        return needs;
    }
}
