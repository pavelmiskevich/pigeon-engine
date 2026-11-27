using System.Text.Json.Serialization;

namespace Pigeon.Persistence.Sqlite.Serialization;

/// <summary>
/// Формат снимка на диске. Отделён от доменного типа, чтобы домен менялся свободно. Поля,
/// которых не было в форматах 1–3, допускают null: так читаются старые снимки.
/// </summary>
/// <param name="Memory">Память мест (формат 4); в формате 4 обязательна, пусть и пустая.</param>
/// <param name="LifeStory">Хроника жизни (формат 4); в формате 4 обязательна — в ней всегда есть рождение.</param>
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
    IReadOnlyList<AffordanceDto>? Affordances = null,
    string? IntentPlace = null,
    long? AbsenceSeconds = null,
    IReadOnlyList<PlaceMemoryDto>? Memory = null,
    IReadOnlyList<LifeEventDto>? LifeStory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Quiet = null);

/// <param name="Familiarity">Знакомство 0..1 на момент <paramref name="LastSeen"/> (секунда жизни голубя).</param>
internal sealed record PlaceMemoryDto(string Place, double Familiarity, long LastSeen, int Visits, bool Territory);

/// <param name="Kind">Число <c>LifeEventKind</c>.</param>
internal sealed record LifeEventDto(
    string Key,
    int Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Place,
    long Tick,
    long LifeSeconds,
    int Version);

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
