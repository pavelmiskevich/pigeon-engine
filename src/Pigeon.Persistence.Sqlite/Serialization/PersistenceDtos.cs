using System.Text.Json.Serialization;

namespace Pigeon.Persistence.Sqlite.Serialization;

/// <summary>
/// Формат снимка на диске. Отделён от доменного типа, чтобы домен менялся свободно. Поля,
/// которых не было в форматах 1 и 2, допускают null: так читаются старые снимки.
/// </summary>
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
    IReadOnlyList<JournalEntryDto> Pending,
    PersonalityDto? Personality = null,
    double? Boredom = null,
    double? Attention = null,
    MoodDto? Mood = null,
    long? IntentTarget = null,
    long? IntentEndsTick = null,
    IReadOnlyList<CooldownDto>? Cooldowns = null,
    IReadOnlyList<AffordanceDto>? Affordances = null);

internal sealed record CooldownDto(int Intent, long UntilTick);

/// <param name="Kind">Число <c>AffordanceKind</c>.</param>
internal sealed record AffordanceDto(
    long Id,
    string PlaceKey,
    int Kind,
    double Attractiveness,
    double Risk,
    long TravelCostTicks);

internal sealed record PersonalityDto(
    int Curiosity,
    int Playfulness,
    int Sociability,
    int Boldness,
    int Calmness,
    int Independence,
    int Energy);

internal sealed record MoodDto(double Valence, double Arousal, double Stress);

internal sealed record RandomStreamDto(string Name, ulong State, ulong Increment);

/// <param name="Payload">Данные входа в формате его кодека; у входов без данных отсутствует.</param>
internal sealed record JournalEntryDto(
    long Tick,
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Payload = null);

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
[JsonSerializable(typeof(IReadOnlyList<AffordanceDto>))]
internal sealed partial class PersistenceJsonContext : JsonSerializerContext;
