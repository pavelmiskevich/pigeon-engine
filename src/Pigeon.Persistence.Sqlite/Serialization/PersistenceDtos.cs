using System.Text.Json.Serialization;

namespace Pigeon.Persistence.Sqlite.Serialization;

/// <summary>Формат снимка на диске. Отделён от доменного типа, чтобы домен менялся свободно.</summary>
internal sealed record SnapshotDto(
    int FormatVersion,
    ulong Seed,
    long StepTicks,
    int MaxTicksPerAdvance,
    int DecisionIntervalTicks,
    long Tick,
    long AccumulatedTicks,
    double Fatigue,
    int Intent,
    long IntentStartedTick,
    IReadOnlyList<RandomStreamDto> RandomStreams,
    long JournalLength,
    IReadOnlyList<JournalEntryDto> Pending);

internal sealed record RandomStreamDto(string Name, ulong State, ulong Increment);

internal sealed record JournalEntryDto(long Tick, string Type);

internal sealed record ArchiveManifestDto(
    string Format,
    int FormatVersion,
    int SchemaVersion,
    int SnapshotFormatVersion,
    Guid PetId,
    long Tick,
    long JournalLength);

/// <summary>Сериализация через source generator: без рефлексии, совместимо с trimming и AOT.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SnapshotDto))]
[JsonSerializable(typeof(ArchiveManifestDto))]
[JsonSerializable(typeof(IReadOnlyList<JournalEntryDto>))]
internal sealed partial class PersistenceJsonContext : JsonSerializerContext;
