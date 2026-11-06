using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.Simulation;

/// <summary>
/// Снимок симуляции (ADR-0017): всё, что нужно, чтобы продолжить её с того же места.
/// Журнал до снимка в него не входит — только его длина; записи после снимка хранятся отдельно.
/// </summary>
/// <param name="FormatVersion">Версия формата снимка; растёт при несовместимом изменении полей.</param>
/// <param name="Accumulated">Время, накопленное планировщиком сверх выполненных шагов.</param>
/// <param name="RandomStreams">Состояния потоков случайности в порядке имён.</param>
/// <param name="JournalLength">Сколько входов применено с начала жизни симуляции.</param>
/// <param name="Pending">Входы, поданные, но ещё не применённые.</param>
/// <param name="Cooldowns">До какого тика недоступны намерения, в порядке намерений.</param>
/// <param name="Affordances">Что мир предлагал при последнем опросе.</param>
public sealed record SimulationSnapshot(
    int FormatVersion,
    ulong Seed,
    SimulationOptions Options,
    long Tick,
    TimeSpan Accumulated,
    PigeonStateSnapshot Pigeon,
    IReadOnlyList<KeyValuePair<string, Pcg32State>> RandomStreams,
    long JournalLength,
    IReadOnlyList<JournalEntry> Pending,
    IReadOnlyList<KeyValuePair<IntentKind, long>> Cooldowns,
    IReadOnlyList<Affordance> Affordances)
{
    /// <summary>
    /// 3 — цель и план намерения, кулдауны и affordances мира (M1.2); 2 — личность, потребности
    /// и настроение (M1.1); 1 — только усталость (S0.6), читается через <see cref="FromVersion1"/>.
    /// </summary>
    public const int CurrentFormatVersion = 3;

    /// <summary>
    /// Снимок формата 1 в текущем формате. В первом формате личности ещё не было, поэтому она
    /// выводится из seed так же, как при рождении; скука и потребность во внимании начинаются с
    /// нуля, настроение — с базовой точки.
    /// </summary>
    public static SimulationSnapshot FromVersion1(
        ulong seed,
        SimulationOptions options,
        long tick,
        TimeSpan accumulated,
        double fatigue,
        IntentKind intent,
        long intentStartedTick,
        IReadOnlyList<KeyValuePair<string, Pcg32State>> randomStreams,
        long journalLength,
        IReadOnlyList<JournalEntry> pending)
    {
        var streams = Common.Randomness.RandomStreams.Restore(seed, randomStreams);
        var born = PigeonSimulation.Born(streams);

        // Плана у намерения в первом формате не было: оно завершается на следующем тике.
        var pigeon = new PigeonStateSnapshot(
            born.Personality,
            Needs.None with { Fatigue = fatigue },
            born.Mood,
            intent,
            intentStartedTick,
            IntentTarget: null,
            IntentEndsTick: Math.Max(tick, intentStartedTick));

        return new SimulationSnapshot(
            CurrentFormatVersion,
            seed,
            options,
            tick,
            accumulated,
            pigeon,
            streams.Snapshot(),
            journalLength,
            pending,
            [],
            []);
    }

    public void Validate()
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"Формат снимка {FormatVersion} не поддерживается; ожидается {CurrentFormatVersion}.");
        }

        ArgumentNullException.ThrowIfNull(Options);
        ArgumentNullException.ThrowIfNull(RandomStreams);
        ArgumentNullException.ThrowIfNull(Pending);
        ArgumentNullException.ThrowIfNull(Cooldowns);
        ArgumentNullException.ThrowIfNull(Affordances);
        Options.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(Tick);
        ArgumentOutOfRangeException.ThrowIfNegative(JournalLength);

        if (Pending.Any(e => e.Tick <= Tick))
        {
            throw new ArgumentException("Неприменённый вход в снимке относится к уже выполненному тику.");
        }
    }
}
