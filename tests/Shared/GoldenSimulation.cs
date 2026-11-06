using Pigeon.Simulation;

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
    /// <summary>Час жизни: голубь успевает устать и отдохнуть (M1.1).</summary>
    public const long Ticks = 36_000;
    public const ulong ExpectedHash = 0xFFDB_991F_037D_605AUL;

    public static JournalEntry[] Journal() =>
    [
        new(123, new UserPoked()),
        new(456, new UserPoked()),
        new(4_567, new UserPoked()),
        new(20_000, new AbsenceEnded(TimeSpan.FromHours(13))),
        new(27_777, new UserPoked()),
    ];

    public static ulong Run() =>
        PigeonSimulation.Replay(Seed, SimulationOptions.Desktop, Journal(), Ticks).ComputeStateHash();
}
