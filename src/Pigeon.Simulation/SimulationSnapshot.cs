using Pigeon.Common.Randomness;
using Pigeon.Core;

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
public sealed record SimulationSnapshot(
    int FormatVersion,
    ulong Seed,
    SimulationOptions Options,
    long Tick,
    TimeSpan Accumulated,
    PigeonStateSnapshot Pigeon,
    IReadOnlyList<KeyValuePair<string, Pcg32State>> RandomStreams,
    long JournalLength,
    IReadOnlyList<JournalEntry> Pending)
{
    public const int CurrentFormatVersion = 1;

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
        Options.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(Tick);
        ArgumentOutOfRangeException.ThrowIfNegative(JournalLength);

        if (Pending.Any(e => e.Tick <= Tick))
        {
            throw new ArgumentException("Неприменённый вход в снимке относится к уже выполненному тику.");
        }
    }
}
