using Pigeon.AI.Behavior;
using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.AI.Tests;

/// <summary>Каталог намерений в JSON (ТЗ §9.2).</summary>
public sealed class BehaviorConfigTests
{
    private const string Minimal = """
        {
          "selection": { "topK": 3, "delta": 0.1, "momentum": 0.25, "travelCostScaleSeconds": 60 },
          "intents": {
            "wander": {
              "considerations": [ { "curve": "constant", "value": 0.3 } ],
              "durationSeconds": [10, 20]
            }
          }
        }
        """;

    [Fact]
    public void DefaultConfig_DescribesTheTenIntentsOfMvp1()
    {
        var config = BehaviorConfig.Default;

        Assert.Empty(config.Problems());
        Assert.Equal(Enum.GetValues<IntentKind>(), config.Intents.Select(i => i.Kind));
        Assert.Equal(3, config.TopK);
        Assert.Equal(0.1, config.Delta);
        Assert.False(config.Find(IntentKind.Rest)!.HasTarget);
        Assert.Equal([AffordanceKind.AttentionTarget], config.Find(IntentKind.SeekAttention)!.Targets);
    }

    [Fact]
    public void Parse_ReadsDurationsCooldownsNeedsAndDefaults()
    {
        var wander = BehaviorConfigJson.Parse(Minimal).Find(IntentKind.Wander)!;

        Assert.Equal(TimeSpan.FromSeconds(10), wander.MinDuration);
        Assert.Equal(TimeSpan.FromSeconds(20), wander.MaxDuration);
        Assert.Equal(TimeSpan.Zero, wander.Cooldown);
        Assert.Equal(NeedEffects.Active, wander.Needs);
        Assert.Empty(wander.TraitWeights);
        Assert.Equal(ResponseCurve.Constant(0.3), wander.Considerations.Single().Curve);
    }

    [Fact]
    public void Parse_IgnoresTheOrderOfIntentsAndTraits()
    {
        const string a = """
            { "selection": { "topK": 2, "delta": 0.1, "momentum": 0, "travelCostScaleSeconds": 30 },
              "intents": {
                "rest":   { "considerations": [ { "input": "need.fatigue", "curve": "linear" } ], "durationSeconds": [1, 2],
                            "traits": { "energy": -0.3, "calmness": 0.2 } },
                "wander": { "considerations": [ { "curve": "constant", "value": 0.3 } ], "durationSeconds": [1, 2] } } }
            """;
        const string b = """
            { "selection": { "topK": 2, "delta": 0.1, "momentum": 0, "travelCostScaleSeconds": 30 },
              "intents": {
                "wander": { "considerations": [ { "curve": "constant", "value": 0.3 } ], "durationSeconds": [1, 2] },
                "rest":   { "considerations": [ { "input": "need.fatigue", "curve": "linear" } ], "durationSeconds": [1, 2],
                            "traits": { "calmness": 0.2, "energy": -0.3 } } } }
            """;

        var first = BehaviorConfigJson.Parse(a);
        var second = BehaviorConfigJson.Parse(b);

        Assert.Equal(first.Intents.Select(i => i.Kind), second.Intents.Select(i => i.Kind));
        Assert.Equal(first.Find(IntentKind.Rest)!.TraitWeights, second.Find(IntentKind.Rest)!.TraitWeights);
    }

    [Fact]
    public void Parse_ListsEveryProblemAtOnce()
    {
        const string broken = """
            {
              "selection": { "topK": 0, "delta": 1.5, "momentum": 0.25, "travelCostScaleSeconds": 60 },
              "intents": {
                "fly": { "considerations": [ { "curve": "constant" } ], "durationSeconds": [1, 2] },
                "rest": {
                  "considerations": [
                    { "input": "need.hunger", "curve": "linear" },
                    { "input": "target.risk", "curve": "linear" },
                    { "input": "need.fatigue", "curve": "wobble" }
                  ],
                  "traits": { "greed": 0.5 },
                  "durationSeconds": [5, 1]
                }
              }
            }
            """;

        var error = Assert.Throws<BehaviorConfigException>(() => BehaviorConfigJson.Parse(broken));

        Assert.Contains(error.Problems, p => p.Contains("«fly»", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.Contains("«need.hunger»", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.Contains("«wobble»", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.Contains("«greed»", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.Contains("требует цели", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.Contains("длительность", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.StartsWith("topK", StringComparison.Ordinal));
        Assert.Contains(error.Problems, p => p.StartsWith("delta", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "selection": { "topK": 3, "delta": 0.1, "momentum": 0, "travelCostScaleSeconds": 60, "topk": 2 }, "intents": {} }""")]
    [InlineData("""{ "selection": { "topK": 3, "delta": 0.1, "momentum": 0, "travelCostScaleSeconds": 60 }, "intents": { "Wander": {} } }""")]
    [InlineData("""{ "intents": {} }""")]
    [InlineData("""не JSON""")]
    public void Parse_RejectsTyposAndBrokenFiles(string json)
    {
        Assert.Throws<BehaviorConfigException>(() => BehaviorConfigJson.Parse(json));
    }
}
