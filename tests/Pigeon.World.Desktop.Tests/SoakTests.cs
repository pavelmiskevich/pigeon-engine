using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.Simulation;
using Pigeon.World.Desktop.Locomotion;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.World.Desktop.Tests;

/// <summary>Отдельно от остальных тестов: замер памяти не должен видеть чужие объекты.</summary>
[CollectionDefinition(nameof(SoakCollection), DisableParallelization = true)]
public sealed class SoakCollection;

/// <summary>
/// Soak-тест ТЗ §38 (#50): сутки жизни ускоренным временем — курсор бродит рядом с голубем, его
/// гладят и тычут, — сохранение раз в 1000 тиков, после него журнал в памяти забывается.
/// Прирост памяти за сутки — не больше 10 МБ (ТЗ §37), журнал в памяти не растёт.
/// </summary>
[Collection(nameof(SoakCollection))]
public sealed class SoakTests
{
    private const long TicksPerDay = 864_000;
    private const int SaveEveryTicks = 1_000;
    private const int RefreshEveryTicks = 20;
    private const long Megabyte = 1024 * 1024;

    [Fact]
    public void DayWithTheCursorAndGestures_KeepsMemoryAndTheJournalBounded()
    {
        var desktop = new FakeDesktop { Topology = FakeDesktop.TwoMonitors(externalScale: 1.25) };
        var world = new DesktopWorld(desktop, FakeDesktop.FullProbe());
        world.Refresh();
        var simulation = new PigeonSimulation(77, SimulationOptions.Desktop);
        simulation.AttachWorld(world);
        var locomotion = new DesktopLocomotion(world, new Pcg32(77, 3));
        var random = new Pcg32(77, 9);
        var now = TimeSpan.Zero;
        var largestJournal = 0;
        long heapAfterWarmup = 0;

        for (long tick = 0; tick < TicksPerDay; tick++)
        {
            if (simulation.Tick % RefreshEveryTicks == 0)
            {
                Report(locomotion.OnWorldChanged(world.Refresh(), now), simulation);
            }

            // Курсор бродит в паре сотен пикселей от голубя и временами уходит.
            var body = locomotion.Body;
            var middle = body.Gait == Gait.Hidden ? ((double X, double Y)?)null : (body.X, body.Y - 35);
            (double X, double Y)? cursor = (tick / 3_000) % 3 == 2
                ? null
                : (body.X + ((random.NextDouble() - 0.5) * 500), body.Y - (random.NextDouble() * 300));
            if (world.ObserveCursor(cursor, middle, now))
            {
                simulation.Submit(new CursorApproached());
            }

            if (tick % 1_777 == 0)
            {
                locomotion.Settle(TimeSpan.FromSeconds(2.5));
                simulation.Submit(new UserPetted());
            }
            else if (tick % 2_311 == 0)
            {
                locomotion.Flinch();
                simulation.Submit(new UserPoked());
            }

            simulation.RunTicks(1);
            var pigeon = simulation.Pigeon;
            locomotion.Execute(pigeon.Intent, pigeon.IntentTarget, pigeon.IntentStartedTick);
            now += SimulationOptions.Desktop.Step;
            Report(locomotion.Update(now), simulation);

            if (simulation.Tick % SaveEveryTicks == 0)
            {
                // Хранилище подтвердило снимок — журнал до него симуляции больше не нужен.
                largestJournal = Math.Max(largestJournal, simulation.Journal.Count);
                simulation.ForgetJournalBefore(simulation.CreateSnapshot().JournalLength);
            }

            if (tick == TicksPerDay / 24)
            {
                heapAfterWarmup = GC.GetTotalMemory(forceFullCollection: true);
            }
        }

        var growth = GC.GetTotalMemory(forceFullCollection: true) - heapAfterWarmup;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Журнал: длина {simulation.JournalLength}, в памяти не больше {largestJournal} за 1000 тиков; прирост кучи {growth / 1024} КБ.");

        Assert.True(simulation.JournalLength > 1_000, "курсор и жесты наполняли журнал");
        Assert.InRange(largestJournal, 1, SaveEveryTicks);
        Assert.True(growth < 10 * Megabyte, $"прирост кучи за сутки {growth / Megabyte} МБ");
    }

    private static void Report(IReadOnlyList<LocomotionEvent> events, PigeonSimulation simulation)
    {
        foreach (var e in events)
        {
            switch (e)
            {
                case ActionReported report:
                    simulation.Submit(new ActionFinished(report.Intent, report.IntentStartedTick, report.Outcome, report.Failure));
                    break;
                case FootingGone gone:
                    simulation.Submit(new FootingLost(gone.Reason));
                    break;
            }
        }
    }
}
