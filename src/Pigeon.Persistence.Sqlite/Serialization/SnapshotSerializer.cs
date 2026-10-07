using System.Text.Json;
using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.Simulation;

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
            entries.Select(e => new JournalEntryDto(e.Tick, InputEventCodec.Encode(e.Event))).ToList(),
            PersistenceJsonContext.Default.IReadOnlyListJournalEntryDto);

    internal static IReadOnlyList<JournalEntry> DeserializeJournal(string json)
    {
        var dtos = JsonSerializer.Deserialize(json, PersistenceJsonContext.Default.IReadOnlyListJournalEntryDto)
            ?? throw new InvalidDataException("Пустой журнал.");
        return [.. dtos.Select(d => new JournalEntry(d.Tick, InputEventCodec.Decode(d.Type)))];
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
            snapshot.Pigeon.Fatigue,
            (int)snapshot.Pigeon.Intent,
            snapshot.Pigeon.IntentStartedTick,
            [.. snapshot.RandomStreams.Select(s => new RandomStreamDto(s.Key, s.Value.State, s.Value.Increment))],
            snapshot.JournalLength,
            [.. snapshot.Pending.Select(e => new JournalEntryDto(e.Tick, InputEventCodec.Encode(e.Event)))]);

    private static SimulationSnapshot FromDto(SnapshotDto dto) =>
        new(
            dto.FormatVersion,
            dto.Seed,
            new SimulationOptions(TimeSpan.FromTicks(dto.StepTicks), dto.MaxTicksPerAdvance, dto.DecisionIntervalTicks),
            dto.Tick,
            TimeSpan.FromTicks(dto.AccumulatedTicks),
            new PigeonStateSnapshot(dto.Fatigue, (IntentKind)dto.Intent, dto.IntentStartedTick),
            [.. (dto.RandomStreams ?? []).Select(s => KeyValuePair.Create(s.Name, new Pcg32State(s.State, s.Increment)))],
            dto.JournalLength,
            [.. (dto.Pending ?? []).Select(e => new JournalEntry(e.Tick, InputEventCodec.Decode(e.Type)))]);
}
