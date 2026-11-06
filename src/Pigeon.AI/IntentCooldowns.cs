using Pigeon.Common.Hashing;
using Pigeon.Core;

namespace Pigeon.AI;

/// <summary>
/// До какого тика каждое намерение недоступно (ТЗ §9.1, Check Cooldown). Хранится отдельно от
/// голубя: это состояние механизма решений, а не часть питомца.
/// </summary>
public sealed class IntentCooldowns
{
    private readonly SortedDictionary<IntentKind, long> _until = [];

    public static IntentCooldowns Restore(IEnumerable<KeyValuePair<IntentKind, long>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var cooldowns = new IntentCooldowns();
        foreach (var (intent, until) in entries)
        {
            if (!Enum.IsDefined(intent))
            {
                throw new ArgumentException($"Неизвестное намерение в кулдаунах: {(int)intent}.", nameof(entries));
            }

            if (!cooldowns._until.TryAdd(intent, until))
            {
                throw new ArgumentException($"Кулдаун {intent} записан дважды.", nameof(entries));
            }
        }

        return cooldowns;
    }

    public bool IsCoolingDown(IntentKind intent, long tick) => _until.TryGetValue(intent, out var until) && tick < until;

    public void Start(IntentKind intent, long untilTick)
    {
        if (untilTick > (_until.TryGetValue(intent, out var current) ? current : long.MinValue))
        {
            _until[intent] = untilTick;
        }
    }

    /// <summary>Забывает истёкшие кулдауны, чтобы снимок и хеш не росли.</summary>
    public void Prune(long tick)
    {
        foreach (var intent in _until.Where(c => c.Value <= tick).Select(c => c.Key).ToList())
        {
            _until.Remove(intent);
        }
    }

    /// <summary>Действующие кулдауны в порядке намерений.</summary>
    public IReadOnlyList<KeyValuePair<IntentKind, long>> Snapshot() => [.. _until];

    public void AddTo(StableHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        hasher.Add(_until.Count);
        foreach (var (intent, until) in _until)
        {
            hasher.Add((int)intent);
            hasher.Add(until);
        }
    }
}
