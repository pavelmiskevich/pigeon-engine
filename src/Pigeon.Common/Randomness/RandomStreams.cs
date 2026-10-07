using Pigeon.Common.Hashing;

namespace Pigeon.Common.Randomness;

/// <summary>
/// Именованные потоки случайности, выведенные из одного корневого seed (ТЗ §3.3).
/// Seed и номер последовательности потока зависят только от корневого seed и имени, поэтому
/// новый случайный вызов в одной подсистеме не сдвигает другие, а порядок создания потоков
/// не важен.
/// </summary>
public sealed class RandomStreams
{
    private readonly ulong _rootSeed;
    private readonly SortedDictionary<string, Pcg32> _streams = new(StringComparer.Ordinal);

    public RandomStreams(ulong rootSeed)
    {
        _rootSeed = rootSeed;
    }

    public ulong RootSeed => _rootSeed;

    /// <summary>Поток с данным именем; создаётся при первом обращении.</summary>
    public IRandomSource Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (!_streams.TryGetValue(name, out var stream))
        {
            var nameHash = StableHasher.HashString(name);
            stream = new Pcg32(SplitMix64.Mix(_rootSeed ^ nameHash), nameHash);
            _streams.Add(name, stream);
        }

        return stream;
    }

    /// <summary>Состояния всех созданных потоков в порядке имён (ordinal).</summary>
    public IReadOnlyList<KeyValuePair<string, Pcg32State>> Snapshot() =>
        _streams.Select(s => KeyValuePair.Create(s.Key, s.Value.State)).ToList();

    public void AddTo(StableHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        hasher.Add(_rootSeed);
        hasher.Add(_streams.Count);
        foreach (var (name, stream) in _streams)
        {
            var state = stream.State;
            hasher.Add(name);
            hasher.Add(state.State);
            hasher.Add(state.Increment);
        }
    }
}
