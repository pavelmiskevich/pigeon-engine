using System.Text.Json;
using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.Simulation;
using Pigeon.World.Abstractions;

namespace Pigeon.Persistence.Sqlite.Serialization;

/// <summary>Перевод снимков и записей журнала в формат хранения и обратно.</summary>
public static class SnapshotSerializer
{
    public static string Serialize(SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(ToDto(snapshot), PersistenceJsonContext.Default.SnapshotDto);
    }

    public static SimulationSnapshot Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        var dto = JsonSerializer.Deserialize(json, PersistenceJsonContext.Default.SnapshotDto)
            ?? throw new InvalidDataException("Пустой снимок.");
        return FromDto(dto);
    }

    internal static string SerializeJournal(IReadOnlyList<JournalEntry> entries) =>
        JsonSerializer.Serialize(
            entries.Select(ToDto).ToList(),
            PersistenceJsonContext.Default.IReadOnlyListJournalEntryDto);

    internal static IReadOnlyList<JournalEntry> DeserializeJournal(string json)
    {
        var dtos = JsonSerializer.Deserialize(json, PersistenceJsonContext.Default.IReadOnlyListJournalEntryDto)
            ?? throw new InvalidDataException("Пустой журнал.");
        return [.. dtos.Select(FromDto)];
    }

    private static SnapshotDto ToDto(SimulationSnapshot snapshot) =>
        new(
            snapshot.FormatVersion,
            snapshot.Seed,
            snapshot.Options.Step.Ticks,
            snapshot.Options.MaxTicksPerAdvance,
            snapshot.Options.DecisionIntervalTicks,
            snapshot.Tick,
            snapshot.Accumulated.Ticks,
            snapshot.Pigeon.Needs.Fatigue,
            (int)snapshot.Pigeon.Intent,
            snapshot.Pigeon.IntentStartedTick,
            [.. snapshot.RandomStreams.Select(s => new RandomStreamDto(s.Key, s.Value.State, s.Value.Increment))],
            snapshot.JournalLength,
            [.. snapshot.Pending.Select(ToDto)],
            ToDto(snapshot.Pigeon.Personality),
            snapshot.Pigeon.Needs.Boredom,
            snapshot.Pigeon.Needs.Attention,
            new MoodDto(snapshot.Pigeon.Mood.Valence, snapshot.Pigeon.Mood.Arousal, snapshot.Pigeon.Mood.Stress),
            snapshot.Pigeon.IntentTarget?.Value,
            snapshot.Pigeon.IntentEndsTick,
            [.. snapshot.Cooldowns.Select(c => new CooldownDto((int)c.Key, c.Value))],
            ToDto(snapshot.Affordances));

    private static SimulationSnapshot FromDto(SnapshotDto dto)
    {
        var options = new SimulationOptions(TimeSpan.FromTicks(dto.StepTicks), dto.MaxTicksPerAdvance, dto.DecisionIntervalTicks);
        var accumulated = TimeSpan.FromTicks(dto.AccumulatedTicks);
        var streams = (dto.RandomStreams ?? []).Select(s => KeyValuePair.Create(s.Name, new Pcg32State(s.State, s.Increment))).ToList();
        var pending = (dto.Pending ?? []).Select(FromDto).ToList();

        if (dto.FormatVersion == 1)
        {
            return SimulationSnapshot.FromVersion1(
                dto.Seed, options, dto.Tick, accumulated, dto.Fatigue, (IntentKind)dto.Intent, dto.IntentStartedTick,
                streams, dto.JournalLength, pending);
        }

        if (dto.FormatVersion is not (2 or SimulationSnapshot.CurrentFormatVersion))
        {
            // Неизвестный формат отклоняет SimulationSnapshot.Validate при восстановлении.
            return new SimulationSnapshot(
                dto.FormatVersion, dto.Seed, options, dto.Tick, accumulated, default, streams, dto.JournalLength, pending, [], []);
        }

        if (dto.Personality is not { } p || dto.Boredom is not { } boredom || dto.Attention is not { } attention
            || dto.Mood is not { } mood)
        {
            throw new InvalidDataException("В снимке нет личности, потребностей или настроения.");
        }

        // В формате 2 у намерения не было цели и плана: оно завершается на следующем тике.
        var pigeon = new PigeonStateSnapshot(
            new Personality(p.Curiosity, p.Playfulness, p.Sociability, p.Boldness, p.Calmness, p.Independence, p.Energy),
            new Needs(dto.Fatigue, boredom, attention),
            new Mood(mood.Valence, mood.Arousal, mood.Stress),
            (IntentKind)dto.Intent,
            dto.IntentStartedTick,
            dto.IntentTarget is { } target ? new AffordanceId(target) : null,
            dto.IntentEndsTick ?? Math.Max(dto.Tick, dto.IntentStartedTick));

        return new SimulationSnapshot(
            SimulationSnapshot.CurrentFormatVersion,
            dto.Seed,
            options,
            dto.Tick,
            accumulated,
            pigeon,
            streams,
            dto.JournalLength,
            pending,
            [.. (dto.Cooldowns ?? []).Select(c => KeyValuePair.Create((IntentKind)c.Intent, c.UntilTick))],
            FromDto(dto.Affordances ?? []));
    }

    internal static string SerializeAffordances(IReadOnlyList<Affordance> affordances) =>
        JsonSerializer.Serialize(ToDto(affordances), PersistenceJsonContext.Default.IReadOnlyListAffordanceDto);

    internal static IReadOnlyList<Affordance> DeserializeAffordances(string json) =>
        FromDto(JsonSerializer.Deserialize(json, PersistenceJsonContext.Default.IReadOnlyListAffordanceDto)
            ?? throw new InvalidDataException("Пустой список affordances."));

    private static List<AffordanceDto> ToDto(IReadOnlyList<Affordance> affordances) =>
        [.. affordances.Select(a => new AffordanceDto(
            a.Id.Value, a.PlaceKey.Value, (int)a.Kind, a.Attractiveness, a.Risk, a.TravelCost.Ticks))];

    private static List<Affordance> FromDto(IReadOnlyList<AffordanceDto> affordances) =>
        [.. affordances.Select(a => new Affordance(
            new AffordanceId(a.Id), new PlaceKey(a.PlaceKey), (AffordanceKind)a.Kind, a.Attractiveness, a.Risk,
            TimeSpan.FromTicks(a.TravelCostTicks)))];

    private static PersonalityDto ToDto(Personality p) =>
        new(p.Curiosity, p.Playfulness, p.Sociability, p.Boldness, p.Calmness, p.Independence, p.Energy);

    private static JournalEntryDto ToDto(JournalEntry entry)
    {
        var encoded = InputEventCodec.Encode(entry.Event);
        return new JournalEntryDto(entry.Tick, encoded.Type, encoded.Payload);
    }

    private static JournalEntry FromDto(JournalEntryDto dto) =>
        new(dto.Tick, InputEventCodec.Decode(dto.Type, dto.Payload));
}
