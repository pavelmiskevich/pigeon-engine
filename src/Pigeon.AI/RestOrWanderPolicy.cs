using Pigeon.Common.Randomness;
using Pigeon.Core;

namespace Pigeon.AI;

/// <summary>
/// Политика MVP-0: выбор между отдыхом и прогулкой по усталости, с гистерезисом и небольшой
/// случайностью. Нужна, чтобы проверить детерминизм цикла, журнала и потоков случайности;
/// Utility AI из ТЗ §9.2 заменяет её в MVP-1.
/// </summary>
public sealed class RestOrWanderPolicy
{
    public const double StartRestAbove = 0.7;
    public const double StopRestBelow = 0.2;
    public const double JitterAmplitude = 0.1;

    public IntentKind Decide(PigeonState pigeon, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(pigeon);
        ArgumentNullException.ThrowIfNull(random);

        var threshold = pigeon.Intent == IntentKind.Rest ? StopRestBelow : StartRestAbove;
        var jitter = (random.NextDouble() - 0.5) * JitterAmplitude;
        return pigeon.Fatigue + jitter > threshold ? IntentKind.Rest : IntentKind.Wander;
    }
}
