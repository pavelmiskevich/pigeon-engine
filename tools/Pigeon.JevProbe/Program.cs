// jev-probe: замер Jev через OpenRouter Decisions API (MVP-0, S0.10; ТЗ §17, §18).
//
//   dotnet run --project tools/Pigeon.JevProbe -c Release -- [параметры]
//
//   --out <dir>           куда писать результаты (artifacts/jev-probe)
//   --models <a,b>        модели (typesafe/jev-1.13,typesafe/jev-1.14)
//   --variants <a,b>      plain — без локальных оценок, scored — с ними (plain,scored)
//   --repeat <n>          повторов каждого сценария (3)
//   --timeout <s>         таймаут клиента, секунды (15) — больше таймаута ТЗ, чтобы увидеть хвост
//   --limit <n>           взять только первые n сценариев
//   --dry-run             без сети и ключа: собрать запросы, проверить privacy-фильтр
//   --report-only         пересобрать summary.md из results.jsonl
//
// Ключ — только из переменной окружения OPENROUTER_API_KEY. В файлы и вывод он не попадает.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pigeon.JevProbe;

const string KeyVariable = "OPENROUTER_API_KEY";

var options = ProbeOptions.Parse(args);
Directory.CreateDirectory(options.OutputDirectory);
var resultsPath = Path.Combine(options.OutputDirectory, "results.jsonl");
var summaryPath = Path.Combine(options.OutputDirectory, "summary.md");
var scenarios = ScenarioCatalog.All.Take(options.Limit).ToList();

if (options.ReportOnly)
{
    var loaded = File.ReadLines(resultsPath)
        .Where(l => l.Length > 0)
        .Select(l => JsonSerializer.Deserialize<ProbeRecord>(l, ProbeJson.Lines)!)
        .ToList();
    File.WriteAllText(summaryPath, Report.Build(loaded));
    Console.WriteLine($"summary: {summaryPath}");
    return 0;
}

// Сборка запросов и проверка privacy-фильтра — до любого обращения к сети.
var requestsDirectory = Path.Combine(options.OutputDirectory, "requests");
Directory.CreateDirectory(requestsDirectory);
foreach (var scenario in scenarios)
{
    foreach (var variant in options.Variants)
    {
        var request = RequestBuilder.Build(options.Models[0], scenario, variant);
        var json = request.ToJsonString(ProbeJson.Indented);
        RequestBuilder.AssertNoRawContext(scenario, json);
        if (scenario.Archetype.Key == "balanced")
        {
            File.WriteAllText(Path.Combine(requestsDirectory, $"{scenario.Situation.Key}.{Name(variant)}.json"), json + "\n");
        }
    }
}

Console.WriteLine($"requests: {scenarios.Count} scenarios × {options.Variants.Count} variants, privacy filter ok");
if (options.DryRun)
{
    return 0;
}

var apiKey = Environment.GetEnvironmentVariable(KeyVariable);
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine($"{KeyVariable} не задан. Ключ читается только из окружения.");
    return 2;
}

using var client = new DecisionsClient(apiKey);
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

File.WriteAllText(Path.Combine(options.OutputDirectory, "run.json"), new JsonObject
{
    ["started_utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    ["runner_os"] = Environment.GetEnvironmentVariable("RUNNER_OS") ?? Environment.OSVersion.Platform.ToString(),
    ["runner_arch"] = Environment.GetEnvironmentVariable("RUNNER_ARCH") ?? System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
    ["models"] = new JsonArray([.. options.Models.Select(m => (JsonNode?)m)]),
    ["variants"] = new JsonArray([.. options.Variants.Select(v => (JsonNode?)Name(v))]),
    ["repeat"] = options.Repeat,
    ["timeout_s"] = options.Timeout.TotalSeconds,
    ["scenarios"] = scenarios.Count,
}.ToJsonString(ProbeJson.Indented) + "\n");

var fixtures = new FixtureWriter(Path.Combine(options.OutputDirectory, "fixtures"), client.Redact);
var records = new List<ProbeRecord>();
var total = options.Repeat * scenarios.Count * options.Variants.Count * options.Models.Count;

await using (var results = new StreamWriter(resultsPath, append: false))
{
    // Модели чередуются внутри сценария, чтобы колебания сети во времени делились поровну.
    for (var repetition = 0; repetition < options.Repeat && !cancel.IsCancellationRequested; repetition++)
    {
        foreach (var scenario in scenarios)
        {
            foreach (var variant in options.Variants)
            {
                foreach (var model in options.Models)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        break;
                    }

                    var request = RequestBuilder.Build(model, scenario, variant);
                    var call = await client.SendAsync(request, options.Timeout, cancel.Token);
                    var record = ToRecord(records.Count, model, variant, scenario, repetition, call);
                    records.Add(record);
                    await results.WriteLineAsync(JsonSerializer.Serialize(record, ProbeJson.Lines));
                    await results.FlushAsync();

                    if (record.IsOk && repetition == 0 && scenario.Archetype.Key == "balanced" && variant == RequestVariant.Plain)
                    {
                        fixtures.Write(model, $"ok-{scenario.Situation.Key}", request, call);
                    }

                    if (records.Count % 50 == 0 || records.Count == total)
                    {
                        Console.WriteLine($"{records.Count}/{total}, errors: {records.Count(r => !r.IsOk)}");
                    }
                }
            }
        }
    }
}

await RunErrorProbesAsync(client, options, fixtures, cancel.Token);

File.WriteAllText(summaryPath, Report.Build(records));
Console.WriteLine($"summary: {summaryPath}");
return 0;

static ProbeRecord ToRecord(int index, string model, RequestVariant variant, Scenario scenario, int repetition, CallResult call)
{
    var record = new ProbeRecord
    {
        Index = index,
        Model = model,
        Variant = Name(variant),
        Archetype = scenario.Archetype.Key,
        Situation = scenario.Situation.Key,
        Repetition = repetition,
        Outcome = call.Outcome switch
        {
            CallOutcome.Ok => Outcomes.Ok,
            CallOutcome.RateLimited => Outcomes.RateLimited,
            CallOutcome.Timeout => Outcomes.Timeout,
            CallOutcome.Network => Outcomes.Network,
            _ => Outcomes.HttpError,
        },
        HttpStatus = call.HttpStatus,
        LatencyMs = Math.Round(call.Latency.TotalMilliseconds, 1),
        Error = call.Outcome == CallOutcome.Ok ? null : Truncate(call.Body),
    };

    if (call.Outcome != CallOutcome.Ok)
    {
        return record;
    }

    try
    {
        var parsed = ResponseParser.Parse(call.Body, scenario);
        return record with
        {
            ModelSnapshot = parsed.ModelSnapshot,
            Provider = parsed.Provider,
            InputTokens = parsed.InputTokens,
            Cost = parsed.Cost,
            Choice = parsed.Choice,
            Probabilities = parsed.Probabilities,
            Confidence = parsed.Confidence,
            React = parsed.React,
            Remember = parsed.Remember,
        };
    }
    catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
    {
        return record with { Outcome = Outcomes.Schema, Error = Truncate($"{ex.Message} | {call.Body}") };
    }
}

// Ответы с ошибками для контрактных тестов: формат ошибок Decisions API тоже часть контракта.
static async Task RunErrorProbesAsync(DecisionsClient client, ProbeOptions options, FixtureWriter fixtures, CancellationToken cancellationToken)
{
    var scenario = ScenarioCatalog.All[0];
    var model = options.Models[0];

    var unknownModel = RequestBuilder.Build("typesafe/does-not-exist", scenario, RequestVariant.Plain);
    var noQuestions = RequestBuilder.Build(model, scenario, RequestVariant.Plain);
    noQuestions["questions"] = new JsonObject();
    var wrongType = RequestBuilder.Build(model, scenario, RequestVariant.Plain);
    wrongType["questions"]![RequestBuilder.IntentQuestion]!["type"] = "pick";

    foreach (var (name, request) in new[] { ("error-unknown-model", unknownModel), ("error-no-questions", noQuestions), ("error-unknown-question-type", wrongType) })
    {
        var call = await client.SendAsync(request, options.Timeout, cancellationToken);
        fixtures.Write(model, name, request, call);
    }

    // Неверный ключ: заведомо фальшивая строка, а не настоящий ключ.
    using var badKey = new DecisionsClient("invalid-key-for-contract-fixture");
    var unauthorized = RequestBuilder.Build(model, scenario, RequestVariant.Plain);
    fixtures.Write(model, "error-unauthorized", unauthorized, await badKey.SendAsync(unauthorized, options.Timeout, cancellationToken));
}

static string Name(RequestVariant variant) => variant.ToString().ToLowerInvariant();

static string Truncate(string text) => text.Length > 2000 ? text[..2000] + "…" : text;

internal sealed record ProbeOptions(
    string OutputDirectory,
    IReadOnlyList<string> Models,
    IReadOnlyList<RequestVariant> Variants,
    int Repeat,
    TimeSpan Timeout,
    int Limit,
    bool DryRun,
    bool ReportOnly)
{
    public static ProbeOptions Parse(string[] args)
    {
        var options = new ProbeOptions(
            Path.Combine("artifacts", "jev-probe"),
            ["typesafe/jev-1.13", "typesafe/jev-1.14"],
            [RequestVariant.Plain, RequestVariant.Scored],
            Repeat: 3,
            TimeSpan.FromSeconds(15),
            int.MaxValue,
            DryRun: false,
            ReportOnly: false);

        for (var i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Нет значения для {args[i]}.");

            options = args[i] switch
            {
                "--out" => options with { OutputDirectory = Value() },
                "--models" => options with { Models = List(Value()) },
                "--variants" => options with { Variants = [.. List(Value()).Select(v => Enum.Parse<RequestVariant>(v, ignoreCase: true))] },
                "--repeat" => options with { Repeat = int.Parse(Value(), CultureInfo.InvariantCulture) },
                "--timeout" => options with { Timeout = TimeSpan.FromSeconds(double.Parse(Value(), CultureInfo.InvariantCulture)) },
                "--limit" => options with { Limit = int.Parse(Value(), CultureInfo.InvariantCulture) },
                "--dry-run" => options with { DryRun = true },
                "--report-only" => options with { ReportOnly = true },
                _ => throw new ArgumentException($"Неизвестный параметр {args[i]}."),
            };
        }

        if (options.Models.Count == 0 || options.Variants.Count == 0 || options.Repeat < 1 || options.Limit < 1)
        {
            throw new ArgumentException("Нужны хотя бы одна модель, один вариант, repeat ≥ 1 и limit ≥ 1.");
        }

        return options;
    }

    private static string[] List(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Пары запрос/ответ для контрактных тестов адаптера (ТЗ §17.4). Ключа в них нет.</summary>
internal sealed class FixtureWriter(string directory, Func<string, string> redact)
{
    public void Write(string model, string name, JsonObject request, CallResult call)
    {
        var target = Path.Combine(directory, model.Replace('/', '_'));
        Directory.CreateDirectory(target);

        JsonNode? body;
        try
        {
            body = JsonNode.Parse(call.Body);
        }
        catch (JsonException)
        {
            body = call.Body;
        }

        // Фикстура не должна связываться с аккаунтом: id генерации и user_id убираются.
        if (body is JsonObject response)
        {
            response.Remove("user_id");
            if (response.ContainsKey("id"))
            {
                response["id"] = "gen-dec-fixture";
            }
        }

        var fixture = new JsonObject
        {
            ["http_status"] = call.HttpStatus,
            ["request"] = request.DeepClone(),
            ["response"] = body,
        };
        File.WriteAllText(Path.Combine(target, $"{name}.json"), redact(fixture.ToJsonString(ProbeJson.Indented)) + "\n");
    }
}
