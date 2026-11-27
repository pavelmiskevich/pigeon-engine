using Pigeon.Common.Hashing;
using Pigeon.Common.Numerics;

namespace Pigeon.Core;

/// <summary>
/// Правила памяти мест (#33, ТЗ §20): прирост знакомства, забывание и его замедление повторами,
/// предел числа записей и порог любимой территории. Время — в секундах жизни голубя.
/// </summary>
/// <param name="VisitGain">Доля оставшегося незнакомства, которую снимает визит места.</param>
/// <param name="AncestorGain">То же для предка места (монитора, на котором оно лежит).</param>
/// <param name="BaseTimeConstantSeconds">Постоянная забывания после первого визита (τ₀).</param>
/// <param name="ConsolidationPerVisit">Во сколько раз каждый следующий визит удлиняет τ.</param>
/// <param name="MaxTimeConstantSeconds">Больше этого τ не растёт.</param>
/// <param name="Capacity">Больше записей память не держит: лишнюю вытесняет наименее знакомая.</param>
/// <param name="FavoriteAt">Знакомство любимой территории не меньше этого…</param>
/// <param name="RivalAtMost">…а у следующей за ней — не больше этого.</param>
/// <param name="DwellGain">
/// Доля оставшегося незнакомства территории, которую снимает минута жизни на ней (#89): дом
/// обживается временем, а не только перелётами.
/// </param>
public sealed record MemoryRules(
    double VisitGain,
    double AncestorGain,
    long BaseTimeConstantSeconds,
    double ConsolidationPerVisit,
    long MaxTimeConstantSeconds,
    int Capacity,
    double FavoriteAt,
    double RivalAtMost,
    double DwellGain = 0.01)
{
    /// <summary>τ₀ = сутки, ×1,5 за визит, не больше 30 суток; 64 места; минута на территории — 1%.</summary>
    public static MemoryRules Default { get; } = new(0.35, 0.1, 86_400, 1.5, 2_592_000, 64, 0.8, 0.5);
}

/// <summary>Запись памяти о месте.</summary>
/// <param name="Familiarity">Знакомство 0..1 на момент <paramref name="LastSeen"/>.</param>
/// <param name="LastSeen">Секунда жизни голубя, когда он был здесь последний раз.</param>
/// <param name="Visits">Полные визиты; визиты через потомка (<see cref="MemoryRules.AncestorGain"/>) не считаются.</param>
/// <param name="Territory">Место — территория (монитор), а не точка на ней.</param>
public readonly record struct PlaceMemoryEntry(PlaceKey Place, double Familiarity, long LastSeen, int Visits, bool Territory);

/// <summary>
/// Насколько голубь знаком с местами (#33, ТЗ §20). Знакомство затухает экспоненциально:
/// <c>f × (1 − 1/τ)^прошедшие секунды</c>, а τ растёт с числом визитов — повторённое
/// забывается медленнее. Считается только четырьмя действиями и
/// <see cref="DeterministicMath.PowInt"/>, поэтому одинаково на всех платформах (ADR-0016).
/// </summary>
public sealed class PlaceMemory
{
    private readonly SortedList<string, PlaceMemoryEntry> _entries = new(StringComparer.Ordinal);

    public PlaceMemory(MemoryRules? rules = null)
    {
        Entries = _entries.Values.AsReadOnly();
        Rules = rules ?? MemoryRules.Default;
        ArgumentOutOfRangeException.ThrowIfLessThan(Rules.Capacity, 1, nameof(rules));
    }

    public MemoryRules Rules { get; }

    /// <summary>Записи в порядке ключа (ordinal) — в этом порядке они входят в снимок и хеш.</summary>
    public IReadOnlyList<PlaceMemoryEntry> Entries { get; }

    /// <exception cref="ArgumentException">Записи повторяются, выходят за пределы или их больше <see cref="MemoryRules.Capacity"/>.</exception>
    public static PlaceMemory FromSnapshot(IReadOnlyList<PlaceMemoryEntry> entries, MemoryRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var memory = new PlaceMemory(rules);
        if (entries.Count > memory.Rules.Capacity)
        {
            throw new ArgumentException($"В памяти {entries.Count} мест, больше предела {memory.Rules.Capacity}.", nameof(entries));
        }

        foreach (var entry in entries)
        {
            if (entry.Place.Value is null)
            {
                throw new ArgumentException("Запись памяти без места.", nameof(entries));
            }

            if (!double.IsFinite(entry.Familiarity) || entry.Familiarity is < 0.0 or > 1.0)
            {
                throw new ArgumentException($"Знакомство с местом {entry.Place} вне 0..1: {entry.Familiarity}.", nameof(entries));
            }

            if (entry.LastSeen < 0 || entry.Visits < 0)
            {
                throw new ArgumentException($"Запись памяти о месте {entry.Place} с отрицательным временем или числом визитов.", nameof(entries));
            }

            if (!memory._entries.TryAdd(entry.Place.Value, entry))
            {
                throw new ArgumentException($"Место {entry.Place} в памяти дважды.", nameof(entries));
            }
        }

        return memory;
    }

    /// <summary>Знакомство с местом в секунду жизни <paramref name="now"/>; незнакомое — 0.</summary>
    public double FamiliarityOf(PlaceKey place, long now) =>
        place.Value is not null && _entries.TryGetValue(place.Value, out var entry) ? Current(entry, now) : 0.0;

    /// <summary>
    /// Голубь побывал в месте: оно знакомится сильно, его предки (<see cref="PlaceKey.Ancestors"/>)
    /// — слабо и становятся территориями.
    /// </summary>
    public void Visit(PlaceKey place, long now, bool territory)
    {
        ArgumentException.ThrowIfNullOrEmpty(place.Value, nameof(place));

        var ancestors = place.Ancestors().ToArray();
        Reinforce(place, now, Rules.VisitGain, countVisit: true, territory, place, ancestors);
        foreach (var ancestor in ancestors)
        {
            Reinforce(ancestor, now, Rules.AncestorGain, countVisit: false, territory: true, place, ancestors);
        }
    }

    /// <summary>
    /// Голубь живёт на территории (#89): она знакомится на <see cref="MemoryRules.DwellGain"/>, визит
    /// не засчитывается. Обживается только территория, уже известная памяти, — новую запись даёт
    /// визит, иначе перелёт на неё потерял бы событие «впервые».
    /// </summary>
    /// <returns><c>false</c> — места нет в памяти или оно не территория.</returns>
    public bool Dwell(PlaceKey territory, long now)
    {
        if (territory.Value is null || !_entries.TryGetValue(territory.Value, out var entry) || !entry.Territory)
        {
            return false;
        }

        Reinforce(territory, now, Rules.DwellGain, countVisit: false, territory: true, territory, []);
        return true;
    }

    /// <summary>
    /// Любимая территория: знакомство не меньше <see cref="MemoryRules.FavoriteAt"/>, а у следующей
    /// по знакомству — не больше <see cref="MemoryRules.RivalAtMost"/>. Пока известна одна
    /// территория, предпочтения нет.
    /// </summary>
    /// <param name="unvisited">
    /// Территории, которые голубь видит в мире (#89): те, которых нет в памяти, соперничают со
    /// знакомством 0 — домосед, не бывавший на другом мониторе, всё равно предпочитает свой.
    /// </param>
    public PlaceKey? Favorite(long now, IEnumerable<PlaceKey>? unvisited = null)
    {
        PlaceMemoryEntry? best = null;
        var bestFamiliarity = -1.0;
        var rivalFamiliarity = -1.0;
        var territories = 0;
        foreach (var entry in _entries.Values)
        {
            if (!entry.Territory)
            {
                continue;
            }

            territories++;
            var familiarity = Current(entry, now);
            if (familiarity > bestFamiliarity)
            {
                rivalFamiliarity = bestFamiliarity;
                bestFamiliarity = familiarity;
                best = entry;
            }
            else if (familiarity > rivalFamiliarity)
            {
                rivalFamiliarity = familiarity;
            }
        }

        foreach (var place in (unvisited ?? []).Distinct())
        {
            if (place.Value is not null && !_entries.ContainsKey(place.Value))
            {
                territories++;
                rivalFamiliarity = Math.Max(rivalFamiliarity, 0.0);
            }
        }

        return territories >= 2 && bestFamiliarity >= Rules.FavoriteAt && rivalFamiliarity <= Rules.RivalAtMost
            ? best!.Value.Place
            : null;
    }

    public void AddTo(StableHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        hasher.Add(_entries.Count);
        foreach (var entry in _entries.Values)
        {
            hasher.Add(entry.Place.Value);
            hasher.Add(entry.Familiarity);
            hasher.Add(entry.LastSeen);
            hasher.Add(entry.Visits);
            hasher.Add(entry.Territory);
        }
    }

    private double Current(PlaceMemoryEntry entry, long now)
    {
        var elapsed = now > entry.LastSeen ? now - entry.LastSeen : 0;
        if (elapsed == 0)
        {
            return entry.Familiarity;
        }

        var consolidation = DeterministicMath.PowInt(Rules.ConsolidationPerVisit, Math.Max(0, entry.Visits - 1));
        var timeConstant = Math.Min(Rules.BaseTimeConstantSeconds * consolidation, Rules.MaxTimeConstantSeconds);
        return entry.Familiarity * DeterministicMath.PowInt(1.0 - 1.0 / timeConstant, elapsed);
    }

    private void Reinforce(PlaceKey place, long now, double gain, bool countVisit, bool territory, PlaceKey visited, PlaceKey[] ancestors)
    {
        if (!_entries.TryGetValue(place.Value, out var entry))
        {
            if (_entries.Count >= Rules.Capacity && !EvictLeastFamiliar(now, visited, ancestors))
            {
                // Всё занято самим визитом и его предками: предел важнее нового места.
                return;
            }

            entry = new PlaceMemoryEntry(place, 0.0, now, 0, false);
        }

        var familiarity = Current(entry, now);
        _entries[place.Value] = entry with
        {
            Familiarity = familiarity + gain * (1.0 - familiarity),
            LastSeen = Math.Max(now, entry.LastSeen),
            Visits = countVisit && entry.Visits < int.MaxValue ? entry.Visits + 1 : entry.Visits,
            Territory = entry.Territory || territory,
        };
    }

    /// <summary>Вытесняет наименее знакомое сейчас место (равенство — меньший ключ), кроме самого визита и его предков.</summary>
    private bool EvictLeastFamiliar(long now, PlaceKey visited, PlaceKey[] ancestors)
    {
        string? weakest = null;
        var weakestFamiliarity = double.MaxValue;
        foreach (var (key, entry) in _entries)
        {
            if (entry.Place == visited || Array.IndexOf(ancestors, entry.Place) >= 0)
            {
                continue;
            }

            // Ключи идут по возрастанию, поэтому при равенстве остаётся первый — меньший.
            var familiarity = Current(entry, now);
            if (familiarity < weakestFamiliarity)
            {
                weakest = key;
                weakestFamiliarity = familiarity;
            }
        }

        return weakest is not null && _entries.Remove(weakest);
    }
}
