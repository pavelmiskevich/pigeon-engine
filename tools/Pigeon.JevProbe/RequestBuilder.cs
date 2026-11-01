using System.Globalization;
using System.Text.Json.Nodes;

namespace Pigeon.JevProbe;

/// <summary>Вариант запроса: передавать ли Jev локальные оценки вариантов (ТЗ §9.2, §17.2).</summary>
internal enum RequestVariant
{
    /// <summary>Только состояние голубя и описания вариантов.</summary>
    Plain,

    /// <summary>Плюс локальная оценка Utility AI по каждому варианту.</summary>
    Scored,
}

/// <summary>
/// Собирает тело запроса Decisions API из сценария. <c>state</c> строится только через
/// privacy-фильтр: категории и метки, без имён приложений, заголовков окон и путей (ТЗ §14).
/// </summary>
internal static class RequestBuilder
{
    public const string IntentQuestion = "intent";
    public const string ReactQuestion = "react";
    public const string RememberQuestion = "remember";

    public static readonly string[] RememberLevels =
    [
        "Forgettable routine moment",
        "Minor moment that fades within the hour",
        "Notable moment worth remembering for a day",
        "Important moment to remember for weeks",
        "Life-defining moment that is never forgotten",
    ];

    public static JsonObject Build(string model, Scenario scenario, RequestVariant variant)
    {
        var questions = new JsonObject
        {
            [IntentQuestion] = new JsonObject
            {
                ["type"] = "choice",
                ["instructions"] =
                    "Which of these options would this particular pigeon most likely do next, given its personality, "
                    + "needs, mood and surroundings described in `pigeon` and `situation`"
                    + (variant == RequestVariant.Scored
                        ? ", taking `instinct` (the pigeon's own preference per option, higher is stronger) into account?"
                        : "?"),
                ["criteria"] = new JsonObject(scenario.Situation.Options
                    .Select(o => KeyValuePair.Create<string, JsonNode?>(o.Key, o.Description))),
            },
        };

        if (scenario.Situation.UnusualEvent is not null)
        {
            questions[ReactQuestion] = new JsonObject
            {
                ["type"] = "noul",
                ["instructions"] = "Would this pigeon visibly react to `situation.unusual_event`?",
                ["criteria"] = new JsonObject
                {
                    ["true"] = "The pigeon notices the event and visibly reacts to it.",
                    ["false"] = "The pigeon does not care and carries on.",
                },
            };
        }

        if (scenario.Situation.SignificantEvent is not null)
        {
            questions[RememberQuestion] = new JsonObject
            {
                ["type"] = "score",
                ["instructions"] = "How important is `situation.significant_event` for this pigeon's memory?",
                ["criteria"] = new JsonArray([.. RememberLevels.Select(l => (JsonNode?)l)]),
            };
        }

        return new JsonObject
        {
            ["model"] = model,
            ["state"] = BuildState(scenario, variant),
            ["questions"] = questions,
        };
    }

    /// <summary>Privacy-фильтр: из сырого контекста в state попадают только разрешённые поля.</summary>
    public static JsonObject BuildState(Scenario scenario, RequestVariant variant)
    {
        var situation = scenario.Situation;
        var personality = new JsonObject();
        foreach (var trait in Traits.All)
        {
            personality[trait] = TraitLabel(scenario.Archetype.Traits[trait]);
        }

        var needs = new JsonObject();
        foreach (var need in Needs.All)
        {
            needs[need] = NeedLabel(situation.Needs[need]);
        }

        var user = situation.User;
        var state = new JsonObject
        {
            ["pigeon"] = new JsonObject
            {
                ["personality"] = personality,
                ["needs"] = needs,
                ["mood"] = situation.Mood,
            },
            ["situation"] = new JsonObject
            {
                ["world"] = "desktop",
                ["time_of_day"] = situation.TimeOfDay,
                ["user"] = new JsonObject
                {
                    ["present"] = user.Present,
                    ["idle"] = IdleLabel(user.IdleMinutes),
                    ["active_app_category"] = user.AppCategory,
                    ["fullscreen"] = user.Fullscreen,
                },
                ["recent_events"] = new JsonArray([.. situation.RecentEvents.Select(e => (JsonNode?)e)]),
            },
        };

        if (situation.UnusualEvent is not null)
        {
            state["situation"]!["unusual_event"] = situation.UnusualEvent;
        }

        if (situation.SignificantEvent is not null)
        {
            state["situation"]!["significant_event"] = situation.SignificantEvent;
        }

        if (variant == RequestVariant.Scored)
        {
            var scores = AutonomousBaseline.Scores(scenario);
            var best = scores.Values.Max();
            state["instinct"] = new JsonObject(scores.Select(s =>
                KeyValuePair.Create<string, JsonNode?>(s.Key, Math.Round(s.Value / best, 2))));
        }

        return state;
    }

    /// <summary>
    /// Проверка фильтра: сырые строки из ОС не должны попасть в тело запроса ни в каком виде.
    /// </summary>
    public static void AssertNoRawContext(Scenario scenario, string requestJson)
    {
        var user = scenario.Situation.User;
        foreach (var raw in new[] { user.AppName, user.WindowTitle })
        {
            if (requestJson.Contains(raw, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Privacy-фильтр пропустил сырое значение в запрос сценария {scenario.Key}.");
            }
        }
    }

    public static string TraitLabel(int value) => value switch
    {
        < 20 => "very low",
        < 40 => "low",
        <= 60 => "medium",
        <= 80 => "high",
        _ => "very high",
    };

    /// <summary>Пороги ТЗ §6.1: comfort &lt; 0.3, want 0.3–0.7, urgent &gt; 0.7.</summary>
    public static string NeedLabel(double urgency) => urgency switch
    {
        < 0.3 => "comfort",
        <= 0.7 => "want",
        _ => "urgent",
    };

    private static string IdleLabel(int minutes) => minutes switch
    {
        < 2 => "active",
        < 15 => "short_break",
        < 60 => "away",
        _ => "long_away",
    };

    public static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
