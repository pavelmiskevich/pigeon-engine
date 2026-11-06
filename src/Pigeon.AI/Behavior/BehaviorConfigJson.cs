using System.Text.Json;
using System.Text.Json.Serialization;
using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.AI.Behavior;

/// <summary>Конфигурация поведения не прочиталась или не прошла проверку; все ошибки — в <see cref="Problems"/>.</summary>
public sealed class BehaviorConfigException : Exception
{
    public BehaviorConfigException(IReadOnlyList<string> problems)
        : base("Конфигурация поведения некорректна:" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", problems))
    {
        Problems = problems;
    }

    public BehaviorConfigException()
        : this(["неизвестная ошибка"])
    {
    }

    public BehaviorConfigException(string message)
        : this([message])
    {
    }

    public BehaviorConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
        Problems = [message];
    }

    public IReadOnlyList<string> Problems { get; } = [];
}

/// <summary>
/// Чтение <see cref="BehaviorConfig"/> из JSON (ТЗ §9.2): настройка поведения — работа с данными.
/// Имена — camelCase, неизвестные поля и имена — ошибка, чтобы опечатка не выключала правило
/// молча. Порядок намерений и трейтов в файле на результат не влияет.
/// </summary>
public static class BehaviorConfigJson
{
    private const string DefaultResource = "Pigeon.AI.behavior.json";

    private static readonly Dictionary<string, ConsiderationInput> Inputs = new(StringComparer.Ordinal)
    {
        ["need.fatigue"] = ConsiderationInput.FatigueNeed,
        ["need.boredom"] = ConsiderationInput.BoredomNeed,
        ["need.attention"] = ConsiderationInput.AttentionNeed,
        ["mood.valence"] = ConsiderationInput.Valence,
        ["mood.arousal"] = ConsiderationInput.Arousal,
        ["mood.stress"] = ConsiderationInput.Stress,
        ["target.attractiveness"] = ConsiderationInput.TargetAttractiveness,
        ["target.risk"] = ConsiderationInput.TargetRisk,
        ["target.travel"] = ConsiderationInput.TargetTravel,
    };

    /// <summary>Имя входа в JSON — оно же ключ фактора в объяснении решения.</summary>
    public static string InputName(ConsiderationInput input) =>
        Inputs.FirstOrDefault(i => i.Value == input).Key ?? input.ToString();

    public static BehaviorConfig Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        BehaviorDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, BehaviorJsonContext.Default.BehaviorDto);
        }
        catch (JsonException e)
        {
            throw new BehaviorConfigException($"JSON не читается: {e.Message}", e);
        }

        var problems = new List<string>();
        var config = FromDto(dto, problems);
        if (config is not null)
        {
            problems.AddRange(config.Problems());
        }

        if (problems.Count > 0 || config is null)
        {
            throw new BehaviorConfigException(problems.Count > 0 ? problems : ["пустая конфигурация"]);
        }

        return config;
    }

    internal static BehaviorConfig LoadDefault()
    {
        using var stream = typeof(BehaviorConfigJson).Assembly.GetManifestResourceStream(DefaultResource)
            ?? throw new InvalidOperationException($"Нет встроенного ресурса {DefaultResource}.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static BehaviorConfig? FromDto(BehaviorDto? dto, List<string> problems)
    {
        if (dto?.Selection is not { } selection || dto.Intents is not { } intents)
        {
            problems.Add("нужны разделы selection и intents");
            return null;
        }

        var definitions = new List<IntentDefinition>();
        foreach (var (name, intent) in intents)
        {
            if (!TryParseEnum<IntentKind>(name, out var kind))
            {
                problems.Add($"неизвестное намерение «{name}»");
                continue;
            }

            definitions.Add(ToDefinition(kind, intent, problems));
        }

        return new BehaviorConfig(
            selection.TopK,
            selection.Delta,
            selection.Momentum,
            Seconds(selection.TravelCostScaleSeconds),
            [.. definitions.OrderBy(d => d.Kind)]);
    }

    private static IntentDefinition ToDefinition(IntentKind kind, IntentDto dto, List<string> problems)
    {
        var prefix = $"{kind}: ";

        var targets = new List<AffordanceKind>();
        foreach (var target in dto.Targets ?? [])
        {
            if (TryParseEnum<AffordanceKind>(target, out var affordance))
            {
                targets.Add(affordance);
            }
            else
            {
                problems.Add($"{prefix}неизвестный тип цели «{target}»");
            }
        }

        var considerations = new List<Consideration>();
        foreach (var c in dto.Considerations ?? [])
        {
            var input = ConsiderationInput.None;
            if (c.Input is not null && !Inputs.TryGetValue(c.Input, out input))
            {
                problems.Add($"{prefix}неизвестный вход «{c.Input}»; есть: {string.Join(", ", Inputs.Keys)}");
                continue;
            }

            if (!TryParseEnum<CurveKind>(c.Curve ?? string.Empty, out var curveKind))
            {
                problems.Add($"{prefix}неизвестная кривая «{c.Curve}»");
                continue;
            }

            considerations.Add(new Consideration(
                input,
                new ResponseCurve(curveKind, c.From ?? 0.0, c.To ?? 1.0, c.Min ?? 0.0, c.Max ?? 1.0, c.Exponent ?? 2, c.Value ?? 1.0)));
        }

        var traits = new List<KeyValuePair<TraitKind, double>>();
        foreach (var (name, weight) in dto.Traits ?? [])
        {
            if (TryParseEnum<TraitKind>(name, out var trait))
            {
                traits.Add(KeyValuePair.Create(trait, weight));
            }
            else
            {
                problems.Add($"{prefix}неизвестный трейт «{name}»");
            }
        }

        if (dto.DurationSeconds is not { Length: 2 } duration)
        {
            problems.Add($"{prefix}durationSeconds — массив из двух чисел [min, max]");
            duration = [1.0, 1.0];
        }

        var needs = dto.Needs ?? new NeedsDto();
        return new IntentDefinition(
            kind,
            [.. targets.Distinct().Order()],
            considerations,
            [.. traits.OrderBy(t => t.Key)],
            dto.Mood?.Arousal ?? 0.0,
            dto.Mood?.Valence ?? 0.0,
            Seconds(duration[0]),
            Seconds(duration[1]),
            Seconds(dto.CooldownSeconds),
            new NeedEffects(needs.Fatigue, needs.Boredom, needs.Attention));
    }

    private static TimeSpan Seconds(double seconds) =>
        double.IsFinite(seconds) && Math.Abs(seconds) < 1e7 ? TimeSpan.FromTicks((long)(seconds * TimeSpan.TicksPerSecond)) : TimeSpan.MinValue;

    /// <summary>Имя в camelCase → значение перечисления; регистр первой буквы не важен, остальной — важен.</summary>
    private static bool TryParseEnum<T>(string name, out T value)
        where T : struct, Enum
    {
        value = default;
        if (string.IsNullOrEmpty(name) || !char.IsAsciiLetterLower(name[0]) || !name.All(char.IsAsciiLetter))
        {
            return false;
        }

        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        return Enum.TryParse(pascal, ignoreCase: false, out value) && Enum.IsDefined(value);
    }
}

internal sealed class BehaviorDto
{
    public SelectionDto? Selection { get; set; }

    public Dictionary<string, IntentDto>? Intents { get; set; }
}

internal sealed class SelectionDto
{
    public int TopK { get; set; }

    public double Delta { get; set; }

    public double Momentum { get; set; }

    public double TravelCostScaleSeconds { get; set; }
}

internal sealed class IntentDto
{
    public string[]? Targets { get; set; }

    public ConsiderationDto[]? Considerations { get; set; }

    public Dictionary<string, double>? Traits { get; set; }

    public MoodWeightsDto? Mood { get; set; }

    public double[]? DurationSeconds { get; set; }

    public double CooldownSeconds { get; set; }

    public NeedsDto? Needs { get; set; }
}

internal sealed class ConsiderationDto
{
    public string? Input { get; set; }

    public string? Curve { get; set; }

    public double? From { get; set; }

    public double? To { get; set; }

    public double? Min { get; set; }

    public double? Max { get; set; }

    public int? Exponent { get; set; }

    public double? Value { get; set; }
}

internal sealed class MoodWeightsDto
{
    public double Arousal { get; set; }

    public double Valence { get; set; }
}

internal sealed class NeedsDto
{
    public double Fatigue { get; set; } = 1.0;

    public double Boredom { get; set; } = 1.0;

    public double Attention { get; set; } = 1.0;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BehaviorDto))]
internal sealed partial class BehaviorJsonContext : JsonSerializerContext;
