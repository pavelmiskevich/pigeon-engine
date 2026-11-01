using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Pigeon.JevProbe;

/// <summary>Одна строка results.jsonl: запрос, исход, задержка и разобранный ответ. Без ключа.</summary>
internal sealed record ProbeRecord
{
    public required int Index { get; init; }
    public required string Model { get; init; }
    public required string Variant { get; init; }
    public required string Archetype { get; init; }
    public required string Situation { get; init; }
    public required int Repetition { get; init; }
    public required string Outcome { get; init; }
    public required int HttpStatus { get; init; }
    public required double LatencyMs { get; init; }
    public string? ModelSnapshot { get; init; }
    public string? Provider { get; init; }
    public int? InputTokens { get; init; }
    public double? Cost { get; init; }
    public string? Choice { get; init; }
    public Dictionary<string, double>? Probabilities { get; init; }
    public double? Confidence { get; init; }
    public double? React { get; init; }
    public double? Remember { get; init; }
    public string? Error { get; init; }

    [JsonIgnore]
    public bool IsOk => Outcome == Outcomes.Ok;
}

internal static class Outcomes
{
    public const string Ok = "ok";
    public const string HttpError = "http_error";
    public const string RateLimited = "rate_limited";
    public const string Timeout = "timeout";
    public const string Network = "network";

    /// <summary>HTTP 200, но ответ не совпал с ожидаемой схемой.</summary>
    public const string Schema = "schema";
}

internal static class ProbeJson
{
    public static readonly JsonSerializerOptions Lines = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions Indented = new(Lines) { WriteIndented = true };
}

/// <summary>
/// Разбор ответа по контракту Decisions API: у каждого ответа есть <c>type</c>; отсутствующий
/// ключ или чужой тип — ошибка, а не значение по умолчанию.
/// </summary>
internal static class ResponseParser
{
    public sealed record Parsed(
        string? ModelSnapshot,
        string? Provider,
        int? InputTokens,
        double? Cost,
        string Choice,
        Dictionary<string, double>? Probabilities,
        double? Confidence,
        double? React,
        double? Remember);

    public static Parsed Parse(string body, Scenario scenario)
    {
        var root = JsonNode.Parse(body)?.AsObject()
            ?? throw new FormatException("Пустой ответ.");
        var answers = root["answers"]?.AsObject()
            ?? throw new FormatException("В ответе нет answers.");

        var intent = Answer(answers, RequestBuilder.IntentQuestion, "choice");
        var choice = intent["choice"]?.GetValue<string>()
            ?? throw new FormatException("В ответе choice нет поля choice.");
        if (scenario.Situation.Options.All(o => o.Key != choice))
        {
            throw new FormatException($"Jev выбрал вариант вне списка: {choice}.");
        }

        Dictionary<string, double>? probabilities = null;
        if (intent["probabilities"] is JsonObject p)
        {
            probabilities = p.ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<double>(), StringComparer.Ordinal);
        }

        double? react = scenario.Situation.UnusualEvent is null
            ? null
            : Number(Answer(answers, RequestBuilder.ReactQuestion, "noul"), "noul");
        double? remember = scenario.Situation.SignificantEvent is null
            ? null
            : Number(Answer(answers, RequestBuilder.RememberQuestion, "score"), "score");

        var usage = root["usage"] as JsonObject;
        return new Parsed(
            root["model"]?.GetValue<string>(),
            root["provider"]?.GetValue<string>(),
            usage?["input_tokens"]?.GetValue<int>(),
            usage?["cost"]?.GetValue<double>(),
            choice,
            probabilities,
            intent["confidence"]?.GetValue<double>(),
            react,
            remember);
    }

    private static JsonObject Answer(JsonObject answers, string key, string type)
    {
        var answer = answers[key] as JsonObject
            ?? throw new FormatException($"Нет ответа на вопрос {key}.");
        var actual = answer["type"]?.GetValue<string>();
        return actual == type
            ? answer
            : throw new FormatException($"Ответ на {key}: тип {actual ?? "null"}, ожидался {type}.");
    }

    private static double Number(JsonObject answer, string field) =>
        answer[field]?.GetValue<double>()
        ?? throw new FormatException($"В ответе нет поля {field}.");
}
