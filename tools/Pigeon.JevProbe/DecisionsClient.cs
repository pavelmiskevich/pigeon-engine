using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace Pigeon.JevProbe;

/// <param name="HttpStatus">Код ответа; 0 — ответа не было (таймаут или сеть).</param>
/// <param name="Body">Тело ответа или текст ошибки, ключ из него вычищен.</param>
internal sealed record CallResult(CallOutcome Outcome, int HttpStatus, TimeSpan Latency, string Body);

internal enum CallOutcome
{
    Ok,
    HttpError,
    RateLimited,
    Timeout,
    Network,
}

/// <summary>
/// Клиент OpenRouter Decisions API (<c>POST /api/alpha/decisions</c>). Один запрос за раз, без
/// повторов: замер видит сырую задержку и сырые ошибки.
/// </summary>
internal sealed class DecisionsClient : IDisposable
{
    public static readonly Uri Endpoint = new("https://openrouter.ai/api/alpha/decisions");

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public DecisionsClient(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey;
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>Время до полного получения тела ответа.</summary>
    public async Task<CallResult> SendAsync(JsonObject request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var content = new StringContent(request.ToJsonString(ProbeJson.Lines), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(Endpoint, content, cts.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var latency = Stopwatch.GetElapsedTime(started);

            var outcome = response.StatusCode switch
            {
                HttpStatusCode.OK => CallOutcome.Ok,
                HttpStatusCode.TooManyRequests => CallOutcome.RateLimited,
                _ => CallOutcome.HttpError,
            };
            return new CallResult(outcome, (int)response.StatusCode, latency, Redact(body));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CallResult(CallOutcome.Timeout, 0, Stopwatch.GetElapsedTime(started), "timeout");
        }
        catch (HttpRequestException ex)
        {
            return new CallResult(CallOutcome.Network, 0, Stopwatch.GetElapsedTime(started), Redact(ex.Message));
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Страховка: ключ не должен попасть ни в один записываемый текст.</summary>
    public string Redact(string text) => Redactor.Redact(text, _apiKey);
}

internal static partial class Redactor
{
    /// <summary>
    /// Убирает ключ и идентификаторы аккаунта OpenRouter: ответы с ошибкой содержат
    /// <c>user_id</c> владельца ключа.
    /// </summary>
    public static string Redact(string text, string? secret)
    {
        var redacted = string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "***", StringComparison.Ordinal);
        return AccountId().Replace(redacted, "user_redacted");
    }

    [GeneratedRegex(@"user_[A-Za-z0-9]{8,}")]
    private static partial Regex AccountId();
}
