using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.World.Abstractions;
using Pigeon.World.Desktop.Interaction;
using Pigeon.World.Desktop.Locomotion;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.World.Desktop.Tests;

/// <summary>Курсор и жесты мышью (ТЗ §8.2, §11.8, M1.6) на фейковом экране.</summary>
public sealed class InteractionTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);
    private const double Scale = 1.75;
    private const double Floor = 1516;
    private const double Home = 2560 * 0.78;

    [Fact]
    public void FarCursor_IsNotATarget()
    {
        var cursor = new CursorPresence();

        Assert.False(cursor.Observe((Home - (300 * Scale), Floor - 40), (Home, Floor), Scale, TimeSpan.Zero));
        Assert.False(cursor.IsNear);
        Assert.Null(cursor.Affordance);
        Assert.Equal((Home - (300 * Scale), Floor - 40), cursor.Position);
    }

    [Fact]
    public void ApproachingCursor_IsNoticedOnceAndBecomesAnAttentionTarget()
    {
        var cursor = new CursorPresence();
        var pigeon = (Home, Floor);
        cursor.Observe((Home - (600 * Scale), Floor), pigeon, Scale, TimeSpan.Zero);

        var noticed = cursor.Observe((Home - (100 * Scale), Floor), pigeon, Scale, TimeSpan.FromSeconds(0.1));
        var again = cursor.Observe((Home - (90 * Scale), Floor), pigeon, Scale, TimeSpan.FromSeconds(0.2));

        Assert.True(noticed);
        Assert.False(again);
        var target = Assert.IsType<Affordance>(cursor.Affordance);
        Assert.Equal(AffordanceKind.AttentionTarget, target.Kind);
        Assert.Equal(CursorPresence.Id, target.Id);
        Assert.Equal(CursorPresence.Place, target.PlaceKey);
        target.Validate();
    }

    [Fact]
    public void CursorThatLeaves_StaysInterestingForAMomentThenGoes()
    {
        var cursor = new CursorPresence();
        var pigeon = (Home, Floor);
        cursor.Observe((Home - (100 * Scale), Floor), pigeon, Scale, TimeSpan.Zero);

        cursor.Observe((Home - (400 * Scale), Floor), pigeon, Scale, TimeSpan.FromSeconds(1));
        Assert.True(cursor.IsNear);
        cursor.Observe((Home - (400 * Scale), Floor), pigeon, Scale, TimeSpan.FromSeconds(2.5));
        Assert.True(cursor.IsNear);
        cursor.Observe((Home - (400 * Scale), Floor), pigeon, Scale, TimeSpan.FromSeconds(3.1));

        Assert.False(cursor.IsNear);
        Assert.Null(cursor.Affordance);
    }

    [Fact]
    public void CursorBetweenTheRadii_KeepsTheTarget()
    {
        var cursor = new CursorPresence();
        var pigeon = (Home, Floor);
        cursor.Observe((Home - (100 * Scale), Floor), pigeon, Scale, TimeSpan.Zero);

        cursor.Observe((Home - (300 * Scale), Floor), pigeon, Scale, TimeSpan.FromSeconds(10));

        Assert.True(cursor.IsNear);
    }

    [Fact]
    public void CursorReturningSoon_IsNotNoticedAgain()
    {
        var cursor = new CursorPresence();
        var pigeon = (Home, Floor);
        var near = (Home - (100 * Scale), Floor);
        var far = (Home - (800 * Scale), Floor);
        cursor.Observe(near, pigeon, Scale, TimeSpan.Zero);
        cursor.Observe(far, pigeon, Scale, TimeSpan.FromSeconds(1));
        cursor.Observe(far, pigeon, Scale, TimeSpan.FromSeconds(3.5));

        Assert.False(cursor.Observe(near, pigeon, Scale, TimeSpan.FromSeconds(4)));
        cursor.Observe(far, pigeon, Scale, TimeSpan.FromSeconds(5));
        cursor.Observe(far, pigeon, Scale, TimeSpan.FromSeconds(7.5));
        Assert.True(cursor.Observe(near, pigeon, Scale, TimeSpan.FromSeconds(16)));
    }

    [Fact]
    public void FastCursor_IsRiskierAndMovingCursorMoreAttractive()
    {
        var slow = new CursorPresence();
        var fast = new CursorPresence();
        var pigeon = (Home, Floor);
        for (var i = 0; i <= 10; i++)
        {
            var t = TimeSpan.FromSeconds(i * 0.05);
            slow.Observe((Home - (150 * Scale) + (i * Scale), Floor), pigeon, Scale, t);
            fast.Observe((Home - (150 * Scale) + ((i % 2) * 100 * Scale), Floor), pigeon, Scale, t);
        }

        Assert.True(fast.Affordance!.Risk >= slow.Affordance!.Risk + 0.3);
        Assert.Equal(0.8, slow.Affordance.Attractiveness);

        slow.Observe((Home - (140 * Scale), Floor), pigeon, Scale, TimeSpan.FromSeconds(3));
        Assert.Equal(0.5, slow.Affordance!.Attractiveness);
    }

    [Fact]
    public void UnknownCursor_OrHiddenPigeon_IsNoTarget()
    {
        var cursor = new CursorPresence();
        cursor.Observe((Home - 50, Floor), (Home, Floor), Scale, TimeSpan.Zero);

        cursor.Observe((Home - 50, Floor), null, Scale, TimeSpan.FromSeconds(0.1));
        Assert.False(cursor.IsNear);

        cursor.Observe(null, (Home, Floor), Scale, TimeSpan.FromSeconds(0.2));
        Assert.Null(cursor.Position);
        Assert.Null(cursor.Affordance);
    }

    [Fact]
    public void World_OffersTheCursorAmongItsAffordances()
    {
        var (world, _) = Create(FakeDesktop.LaptopOnly());
        var surfaces = world.Current().Count;

        world.ObserveCursor((Home - 100, Floor - 40), (Home, Floor - 40), TimeSpan.Zero);

        Assert.Equal(surfaces + 1, world.Current().Count);
        Assert.Contains(world.Current(), a => a.Id == CursorPresence.Id);
        Assert.Equal(world.Current().OrderBy(a => a.Id.Value), world.Current());
    }

    [Fact]
    public void Click_IsAPoke()
    {
        var gestures = new PointerGestures();

        Assert.Empty(gestures.Press(100, 100, TimeSpan.Zero));
        Assert.Empty(gestures.Move(103, 102, TimeSpan.FromMilliseconds(50)));

        Assert.Equal([new Gesture(GestureKind.Poke, 103, 102)], gestures.Release(103, 102, TimeSpan.FromMilliseconds(120)));
        Assert.False(gestures.IsCarrying);
    }

    [Fact]
    public void PressAndPull_PicksUpCarriesAndDrops()
    {
        var gestures = new PointerGestures();
        gestures.Press(100, 100, TimeSpan.Zero);

        var picked = gestures.Move(120, 90, TimeSpan.FromMilliseconds(50));
        var carried = gestures.Move(300, 50, TimeSpan.FromMilliseconds(80));
        var dropped = gestures.Release(310, 60, TimeSpan.FromMilliseconds(400));

        Assert.Equal([new Gesture(GestureKind.PickUp, 100, 100), new Gesture(GestureKind.Carry, 120, 90)], picked);
        Assert.Equal([new Gesture(GestureKind.Carry, 300, 50)], carried);
        Assert.Equal([new Gesture(GestureKind.Drop, 310, 60)], dropped);
        Assert.False(gestures.IsCarrying);
    }

    [Fact]
    public void LostCapture_DropsACarriedPigeonButIsNoPoke()
    {
        var carrying = new PointerGestures();
        carrying.Press(100, 100, TimeSpan.Zero);
        carrying.Move(200, 100, TimeSpan.FromMilliseconds(50));
        var clicking = new PointerGestures();
        clicking.Press(100, 100, TimeSpan.Zero);

        Assert.Equal([new Gesture(GestureKind.Drop, 210, 100)], carrying.Cancel(210, 100));
        Assert.Empty(clicking.Cancel(100, 100));
        Assert.Empty(clicking.Release(100, 100, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void RubbingBackAndForth_IsPettingAtMostEverySecondAndAHalf()
    {
        var gestures = new PointerGestures();
        var pets = new List<TimeSpan>();
        var now = TimeSpan.Zero;

        // Курсор ходит туда-сюда по 30 пикселей, смена направления каждые 150 мс, 4 секунды.
        for (var i = 0; i < 80; i++)
        {
            now += TimeSpan.FromMilliseconds(50);
            var x = 100 + (10 * ((i / 3 % 2 == 0) ? i % 3 : 3 - (i % 3)));
            pets.AddRange(gestures.Move(x, 50, now).Where(g => g.Kind == GestureKind.Pet).Select(_ => now));
        }

        Assert.InRange(pets.Count, 2, 3);
        Assert.True(pets[0] < TimeSpan.FromSeconds(1));
        Assert.All(pets.Zip(pets.Skip(1)), p => Assert.True(p.Second - p.First >= TimeSpan.FromSeconds(1.5)));
    }

    [Fact]
    public void SlowPassesOrOneWayMoves_AreNotPetting()
    {
        var oneWay = new PointerGestures();
        var slow = new PointerGestures();

        for (var i = 0; i < 40; i++)
        {
            Assert.Empty(oneWay.Move(100 + (i * 10), 50, TimeSpan.FromMilliseconds(50 * i)));

            // Смена направления раз в секунду — это не поглаживание.
            var x = 100 + (20 * ((i / 10 % 2 == 0) ? i % 10 : 10 - (i % 10)));
            Assert.Empty(slow.Move(x, 50, TimeSpan.FromMilliseconds(100 * i)));
        }
    }

    [Fact]
    public void HeldPigeon_HangsFromTheCursorFlappingAndDoesNotWalk()
    {
        var (world, locomotion) = Create(FakeDesktop.LaptopOnly());
        locomotion.Update(TimeSpan.Zero);
        locomotion.Execute(IntentKind.Wander, null, 1);

        locomotion.PickUp(Home + 10, Floor - 30, TimeSpan.FromSeconds(1));
        locomotion.Carry(Home + 410, Floor - 530);
        locomotion.Update(TimeSpan.FromSeconds(2));
        locomotion.Update(TimeSpan.FromSeconds(3));

        Assert.True(locomotion.IsHeld);
        Assert.Equal(Gait.Held, locomotion.Body.Gait);
        Assert.Equal(Home + 400, locomotion.Body.X, 1e-9);
        Assert.Equal(Floor - 500, locomotion.Body.Y, 1e-9);
        Assert.Equal(Floor, locomotion.Body.GroundY);
        Assert.False(locomotion.Body.FacingLeft);
    }

    [Fact]
    public void DroppedPigeon_FallsOntoTheSurfaceBelowAndCarriesOn()
    {
        var (_, locomotion) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        run.For(Frame);
        locomotion.Execute(IntentKind.Rest, null, 1);
        locomotion.PickUp(Home, Floor - 30, run.Now);
        locomotion.Carry(1000, Floor - 630);

        locomotion.Drop(run.Now);
        run.For(TimeSpan.FromSeconds(3));

        Assert.Equal([Gait.Fly, Gait.Land, Gait.Stand], Phases(run.Gaits.SkipWhile(g => g == Gait.Stand)));
        Assert.Equal(1000, locomotion.Body.X, 1e-9);
        Assert.Equal(Floor, locomotion.Body.Y);
        Assert.False(locomotion.IsHeld);
    }

    [Fact]
    public void PigeonDroppedOnAnotherMonitor_LivesThere()
    {
        var (world, locomotion) = Create(FakeDesktop.TwoMonitors());
        var run = new Run(locomotion);
        run.For(Frame);
        locomotion.PickUp(locomotion.Body.X, locomotion.Body.Y - 30, run.Now);
        locomotion.Carry(3500, 600);

        locomotion.Drop(run.Now);
        run.For(TimeSpan.FromSeconds(4));

        Assert.Equal(FakeDesktop.External, world.PigeonMonitor);
        Assert.Equal(FakeDesktop.External, locomotion.Body.Monitor);
        Assert.Equal(Gait.Stand, locomotion.Body.Gait);
    }

    [Fact]
    public void Poke_MakesAStandingPigeonHopAndCarryOn()
    {
        var (_, locomotion) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        run.For(Frame);
        var x = locomotion.Body.X;

        locomotion.Flinch();
        run.For(TimeSpan.FromSeconds(1));

        Assert.Equal([Gait.Hop, Gait.Stand], Phases(run.Gaits.Skip(1)));
        Assert.Equal(x, locomotion.Body.X);
    }

    [Fact]
    public void Observing_TheCursor_TurnsTowardsIt()
    {
        var (world, locomotion) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        run.For(Frame);
        Assert.True(locomotion.Body.FacingLeft);
        world.ObserveCursor((Home + 200, Floor - 100), (Home, Floor - 40), run.Now);

        locomotion.Execute(IntentKind.Observe, CursorPresence.Id, 5);
        run.For(TimeSpan.FromSeconds(2));

        Assert.False(locomotion.Body.FacingLeft);
        Assert.All(run.Bodies.Skip(1), b => Assert.Equal(Gait.Stand, b.Gait));
        Assert.Equal(Home, locomotion.Body.X);
    }

    [Fact]
    public void Following_TheCursor_WalksUpToIt()
    {
        var (world, locomotion) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        run.For(Frame);
        world.ObserveCursor((Home - (200 * Scale), Floor - 100), (Home, Floor - 40), run.Now);

        locomotion.Execute(IntentKind.FollowTarget, CursorPresence.Id, 5);
        run.For(TimeSpan.FromSeconds(12));

        Assert.Contains(Gait.Walk, run.Gaits);
        Assert.DoesNotContain(Gait.Fly, run.Gaits);
        Assert.Equal(Home - (155 * Scale), locomotion.Body.X, 2 * Scale);
        Assert.Equal(Gait.Stand, locomotion.Body.Gait);
    }

    [Fact]
    public void FarCursor_IsReachedByFlying()
    {
        var (world, locomotion) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        run.For(Frame);
        world.ObserveCursor((300, Floor - 100), (Home, Floor - 40), run.Now);

        locomotion.Execute(IntentKind.SeekAttention, CursorPresence.Id, 5);
        run.For(TimeSpan.FromSeconds(8));

        Assert.Contains(Gait.Fly, run.Gaits);
        Assert.Equal(300 + (45 * Scale), locomotion.Body.X, 2 * Scale);
    }

    [Fact]
    public void Playing_ByTheCursor_HopsAgainAndAgain()
    {
        var (world, locomotion) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        run.For(Frame);
        world.ObserveCursor((Home - (45 * Scale), Floor - 60), (Home, Floor - 40), run.Now);

        locomotion.Execute(IntentKind.Play, CursorPresence.Id, 5);
        run.For(TimeSpan.FromSeconds(5));

        Assert.True(Phases(run.Gaits).Count(g => g == Gait.Hop) >= 3);
        Assert.True(locomotion.Body.FacingLeft);
    }

    [Fact]
    public void Fleeing_TheCursor_FliesAwayAndReportsDone()
    {
        var (world, locomotion) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        run.For(Frame);
        world.ObserveCursor((Home - 80, Floor - 60), (Home, Floor - 40), run.Now);

        locomotion.Execute(IntentKind.Flee, CursorPresence.Id, 5);
        run.For(TimeSpan.FromSeconds(5));

        Assert.Equal([Gait.Stand, Gait.TakeOff, Gait.Fly, Gait.Land, Gait.Stand], Phases(run.Gaits));
        Assert.Equal(2560 - (60 * Scale), locomotion.Body.X, 1e-6);
        var report = Assert.Single(run.Events.OfType<ActionReported>());
        Assert.Equal(ActionOutcome.Completed, report.Outcome);
    }

    [Fact]
    public void CorneredPigeon_FleesPastTheCursor()
    {
        var (world, locomotion) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        run.For(Frame);
        locomotion.PickUp(Home, Floor - 30, run.Now);
        locomotion.Carry(2400, Floor - 30);
        locomotion.Drop(run.Now);
        run.For(TimeSpan.FromSeconds(2));
        world.ObserveCursor((2300, Floor - 60), (locomotion.Body.X, Floor - 40), run.Now);

        locomotion.Execute(IntentKind.Flee, CursorPresence.Id, 5);
        run.For(TimeSpan.FromSeconds(5));

        Assert.True(locomotion.Body.X < 2300 - (300 * Scale));
    }

    private static List<Gait> Phases(IEnumerable<Gait> gaits)
    {
        var phases = new List<Gait>();
        foreach (var gait in gaits)
        {
            if (phases.Count == 0 || phases[^1] != gait)
            {
                phases.Add(gait);
            }
        }

        return phases;
    }

    private static (DesktopWorld World, DesktopLocomotion Locomotion) Create(DesktopTopology topology)
    {
        var world = new DesktopWorld(new FakeDesktop { Topology = topology }, FakeDesktop.FullProbe());
        world.Refresh();
        return (world, new DesktopLocomotion(world, new Pcg32(1, 2)));
    }

    /// <summary>Прогон локомоции кадрами по 1/60 с с записью тела и событий.</summary>
    private sealed class Run(DesktopLocomotion locomotion)
    {
        public TimeSpan Now { get; private set; }

        public List<PigeonBody> Bodies { get; } = [];

        public List<LocomotionEvent> Events { get; } = [];

        public IEnumerable<Gait> Gaits => Bodies.Select(b => b.Gait);

        public void For(TimeSpan duration)
        {
            var end = Now + duration;
            while (Now < end)
            {
                Now += Frame;
                Events.AddRange(locomotion.Update(Now));
                Bodies.Add(locomotion.Body);
            }
        }
    }
}
