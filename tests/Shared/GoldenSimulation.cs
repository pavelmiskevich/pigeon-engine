using Pigeon.Core;
using Pigeon.Simulation;
using Pigeon.World.Abstractions;

namespace Pigeon.Testing;

/// <summary>
/// Эталонный прогон симуляции (ТЗ §3.3, ADR-0016). Файл подключается ссылкой и в .NET-тесты,
/// и в браузерный харнесс, поэтому сценарий и ожидаемый хеш везде одни и те же.
/// </summary>
/// <remarks>
/// Если меняется логика симуляции, хеш законно меняется: новое значение фиксируется в том же
/// PR с объяснением. Если хеш разошёлся только на одной платформе или в WASM — это нарушение
/// детерминизма, а не повод обновить эталон.
/// </remarks>
internal static class GoldenSimulation
{
    public const ulong Seed = 0x5EED_0001UL;

    /// <summary>Час жизни: голубь устаёт, отдыхает, выбирает цели в мире и теряет опору (M1.1–M1.3).</summary>
    public const long Ticks = 36_000;
    public const ulong ExpectedHash = 0x4AF1_542C_DF92_E351UL;

    public static JournalEntry[] Journal() =>
    [
        new(1, new AffordancesObserved(World)),
        new(123, new UserPoked()),
        new(456, new UserPoked()),
        new(4_567, new UserPoked()),
        new(12_000, new AffordancesObserved(World[1..])),
        new(20_000, new AbsenceEnded(TimeSpan.FromHours(13))),
        new(24_000, new AffordancesObserved(World)),
        new(27_777, new UserPoked()),
        new(30_000, new FootingLost(DisplacementReason.TerritoryGone)),
        new(30_000, new AffordancesObserved(World[..2])),
    ];

    /// <summary>Мир эталона: точка интереса, насест, курсор и второй монитор.</summary>
    private static Affordance[] World =>
    [
        new(new AffordanceId(1), new PlaceKey("poi:window"), AffordanceKind.PointOfInterest, 0.6, 0.1, TimeSpan.FromSeconds(10)),
        new(new AffordanceId(2), new PlaceKey("perch:taskbar"), AffordanceKind.PerchSpot, 0.5, 0.0, TimeSpan.FromSeconds(5)),
        new(new AffordanceId(3), new PlaceKey("cursor"), AffordanceKind.AttentionTarget, 0.7, 0.2, TimeSpan.FromSeconds(8)),
        new(new AffordanceId(4), new PlaceKey("monitor:2"), AffordanceKind.TerritoryZone, 0.6, 0.1, TimeSpan.FromSeconds(30)),
    ];

    public static ulong Run() =>
        PigeonSimulation.Replay(Seed, SimulationOptions.Desktop, Journal(), Ticks).ComputeStateHash();
}
