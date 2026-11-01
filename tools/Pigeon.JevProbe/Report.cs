using System.Globalization;
using System.Text;

namespace Pigeon.JevProbe;

/// <summary>Сводка замера в Markdown: задержка, ошибки, токены, стоимость, характерность выбора.</summary>
internal static class Report
{
    /// <summary>Таймаут из ТЗ §18, который проверяет замер.</summary>
    public static readonly TimeSpan SpecTimeout = TimeSpan.FromSeconds(3);

    /// <summary>ε и w из ТЗ §17.4.</summary>
    public const double BlendEpsilon = 0.05;
    public static readonly double[] BlendWeights = [0.5, 1.0];

    /// <summary>Режимы частоты ТЗ §18 при 8 часах в день, 30 дней.</summary>
    private const double AdaptiveRequestsPerMonth = 1.0 * 60 * 8 * 30;
    private const double HighRequestsPerMonth = 6.0 * 60 * 8 * 30;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Build(IReadOnlyList<ProbeRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Замер Jev — сводка");
        sb.AppendLine();
        sb.AppendLine(Inv, $"Запросов: {records.Count}. Сценариев: {ScenarioCatalog.All.Count} "
            + $"({ScenarioCatalog.Archetypes.Count} архетипов × {ScenarioCatalog.Situations.Count} ситуаций).");
        sb.AppendLine();

        AppendLatency(sb, records);
        AppendTokensAndCost(sb, records);
        AppendCharacter(sb, records);
        AppendEvents(sb, records);
        AppendSituationTables(sb, records);
        AppendErrors(sb, records);
        return sb.ToString();
    }

    private static void AppendLatency(StringBuilder sb, IReadOnlyList<ProbeRecord> records)
    {
        sb.AppendLine("## Задержка и ошибки");
        sb.AppendLine();
        sb.AppendLine("Задержка — до полного получения ответа, только успешные запросы, мс.");
        sb.AppendLine();
        sb.AppendLine("| Модель | Снимок | Запросов | Успешно | Ошибки | p50 | p90 | p95 | p99 | max | > 1 с | > 3 с | первый |");
        sb.AppendLine("|--------|--------|---------:|--------:|--------|----:|----:|----:|----:|----:|------:|------:|-------:|");
        foreach (var group in records.GroupBy(r => r.Model).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ok = group.Where(r => r.IsOk).Select(r => r.LatencyMs).Order().ToList();
            var errors = string.Join(", ", group.Where(r => !r.IsOk)
                .GroupBy(r => r.Outcome == Outcomes.HttpError ? $"http {r.HttpStatus}" : r.Outcome)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}: {g.Count()}"));
            var snapshots = string.Join(", ", group.Select(r => r.ModelSnapshot).OfType<string>().Distinct().Order(StringComparer.Ordinal));
            var overSecond = group.Count(r => r.LatencyMs > 1000 || r.Outcome == Outcomes.Timeout);
            var slow = group.Count(r => r.LatencyMs > SpecTimeout.TotalMilliseconds || r.Outcome == Outcomes.Timeout);
            var first = group.MinBy(r => r.Index)!;

            sb.AppendLine(Inv, $"| `{group.Key}` | {snapshots} | {group.Count()} | {ok.Count} | {(errors.Length == 0 ? "—" : errors)} "
                + $"| {Ms(Percentile(ok, 50))} | {Ms(Percentile(ok, 90))} | {Ms(Percentile(ok, 95))} | {Ms(Percentile(ok, 99))} "
                + $"| {Ms(ok.LastOrDefault())} | {Pct(overSecond, group.Count())} | {Pct(slow, group.Count())} | {Ms(first.LatencyMs)} |");
        }

        sb.AppendLine();
    }

    private static void AppendTokensAndCost(StringBuilder sb, IReadOnlyList<ProbeRecord> records)
    {
        sb.AppendLine("## Размер state и стоимость");
        sb.AppendLine();
        sb.AppendLine("| Модель | Вариант | input_tokens (мин / сред / макс) | Стоимость всего, $ | $ за запрос | Adaptive, $/мес | High, $/мес |");
        sb.AppendLine("|--------|---------|------------------|------:|------:|------:|------:|");
        foreach (var group in records.Where(r => r.IsOk)
            .GroupBy(r => (r.Model, r.Variant))
            .OrderBy(g => g.Key.Model, StringComparer.Ordinal).ThenBy(g => g.Key.Variant, StringComparer.Ordinal))
        {
            var tokens = group.Select(r => r.InputTokens).OfType<int>().ToList();
            var costs = group.Select(r => r.Cost).OfType<double>().ToList();
            var mean = costs.Count == 0 ? 0 : costs.Average();
            sb.AppendLine(Inv, $"| `{group.Key.Model}` | {group.Key.Variant} "
                + $"| {(tokens.Count == 0 ? "—" : $"{tokens.Min()} / {tokens.Average():0} / {tokens.Max()}")} "
                + $"| {costs.Sum():0.######} | {mean:0.#########} | {mean * AdaptiveRequestsPerMonth:0.###} | {mean * HighRequestsPerMonth:0.###} |");
        }

        sb.AppendLine();
        sb.AppendLine("Adaptive — 1 запрос в минуту, High — 6 в минуту, оба при 8 часах в день 30 дней (ТЗ §18).");
        sb.AppendLine();
    }

    private static void AppendCharacter(StringBuilder sb, IReadOnlyList<ProbeRecord> records)
    {
        sb.AppendLine("## Характерность выбора: Jev против Autonomous");
        sb.AppendLine();
        sb.AppendLine("- **Разброс по архетипам** — среднее попарное расхождение Йенсена — Шеннона (log2, 0..1) между "
            + "распределениями выбора разных архетипов в одной ситуации. Больше — выбор сильнее зависит от личности.");
        sb.AppendLine("- **Разных лидеров** — сколько разных вариантов-лидеров дают 10 архетипов в одной ситуации, среднее по ситуациям.");
        sb.AppendLine("- **Совпадение** — лидер Jev совпадает с лидером Autonomous; **в пуле** — лидер Jev входит в лучшие K Autonomous.");
        sb.AppendLine(Inv, $"- **Смена после смешивания** — `utility × (ε + p)^w` при ε = {BlendEpsilon} и w = 0.5 или 1 (ТЗ §17.4) меняет лидера Utility.");
        sb.AppendLine("- **Нестабильность** — доля сценариев, где лидер Jev различается между повторами; **max Δp** — средний максимальный разброс вероятности.");
        sb.AppendLine();
        sb.AppendLine("| Источник | Вариант | Разброс по архетипам | Разных лидеров | Совпадение | В пуле | Смена, w = 0.5 | Смена, w = 1 | confidence | Нестабильность | max Δp |");
        sb.AppendLine("|----------|---------|------:|------:|------:|------:|------:|------:|------:|------:|------:|");

        var autonomous = ScenarioCatalog.All.ToDictionary(s => s.Key, AutonomousBaseline.Distribution);
        var (autoSpread, autoLeaders) = Spread(autonomous);
        sb.AppendLine(Inv, $"| Autonomous | — | {autoSpread:0.000} | {autoLeaders:0.0} | — | — | — | — | — | — | — |");

        foreach (var group in records.Where(r => r.IsOk)
            .GroupBy(r => (r.Model, r.Variant))
            .OrderBy(g => g.Key.Model, StringComparer.Ordinal).ThenBy(g => g.Key.Variant, StringComparer.Ordinal))
        {
            var byScenario = group.GroupBy(r => $"{r.Archetype}/{r.Situation}").ToDictionary(g => g.Key, g => g.ToList());
            var jev = byScenario.ToDictionary(kv => kv.Key, kv => MeanDistribution(kv.Value));
            var (spread, leaders) = Spread(jev);

            int agree = 0, inPool = 0, unstable = 0;
            var blendChanged = new int[BlendWeights.Length];
            double maxDeltaSum = 0;
            foreach (var scenario in ScenarioCatalog.All.Where(s => jev.ContainsKey(s.Key)))
            {
                var p = jev[scenario.Key];
                var auto = autonomous[scenario.Key];
                var scores = AutonomousBaseline.Scores(scenario);
                var jevLeader = Leader(p);
                agree += jevLeader == Leader(auto) ? 1 : 0;
                inPool += auto[jevLeader] > 0 ? 1 : 0;

                for (var w = 0; w < BlendWeights.Length; w++)
                {
                    var weight = BlendWeights[w];
                    var blended = scores.ToDictionary(s => s.Key, s => s.Value * Math.Pow(BlendEpsilon + p.GetValueOrDefault(s.Key), weight), StringComparer.Ordinal);
                    blendChanged[w] += Leader(blended) == Leader(scores) ? 0 : 1;
                }

                var runs = byScenario[scenario.Key];
                unstable += runs.Select(r => r.Choice).Distinct().Count() > 1 ? 1 : 0;
                maxDeltaSum += scenario.Situation.Options.Max(o =>
                {
                    var values = runs.Select(r => Distribution(r).GetValueOrDefault(o.Key)).ToList();
                    return values.Max() - values.Min();
                });
            }

            var n = jev.Count;
            var confidence = group.Select(r => r.Confidence).OfType<double>().DefaultIfEmpty(double.NaN).Average();
            sb.AppendLine(Inv, $"| `{group.Key.Model}` | {group.Key.Variant} | {spread:0.000} | {leaders:0.0} | {Pct(agree, n)} | {Pct(inPool, n)} "
                + $"| {string.Join(" | ", blendChanged.Select(c => Pct(c, n)))} | {confidence:0.00} | {Pct(unstable, n)} | {maxDeltaSum / Math.Max(1, n):0.000} |");
        }

        sb.AppendLine();
    }

    private static void AppendEvents(StringBuilder sb, IReadOnlyList<ProbeRecord> records)
    {
        sb.AppendLine("## Вопросы react (noul) и remember (score)");
        sb.AppendLine();
        sb.AppendLine("Среднее по повторам. react — вероятность «да», remember — позиция на шкале 0–4.");
        sb.AppendLine();

        var models = records.Where(r => r.IsOk).Select(r => (r.Model, r.Variant)).Distinct()
            .OrderBy(m => m.Model, StringComparer.Ordinal).ThenBy(m => m.Variant, StringComparer.Ordinal).ToList();
        var header = string.Join(" | ", ScenarioCatalog.Archetypes.Select(a => a.Key));

        foreach (var (title, situations, select) in new (string, IEnumerable<Situation>, Func<ProbeRecord, double?>)[]
        {
            ("react", ScenarioCatalog.Situations.Where(s => s.UnusualEvent is not null), r => r.React),
            ("remember", ScenarioCatalog.Situations.Where(s => s.SignificantEvent is not null), r => r.Remember),
        })
        {
            sb.AppendLine(Inv, $"### {title}");
            sb.AppendLine();
            sb.AppendLine(Inv, $"| Модель | Вариант | Ситуация | {header} |");
            sb.AppendLine(Inv, $"|---|---|---|{string.Concat(Enumerable.Repeat("---:|", ScenarioCatalog.Archetypes.Count))}");
            foreach (var (model, variant) in models)
            {
                foreach (var situation in situations)
                {
                    var cells = ScenarioCatalog.Archetypes.Select(a =>
                    {
                        var values = records.Where(r => r.IsOk && r.Model == model && r.Variant == variant
                                && r.Archetype == a.Key && r.Situation == situation.Key)
                            .Select(select).OfType<double>().ToList();
                        return values.Count == 0 ? "—" : values.Average().ToString("0.00", Inv);
                    });
                    sb.AppendLine(Inv, $"| `{model}` | {variant} | {situation.Key} | {string.Join(" | ", cells)} |");
                }
            }

            sb.AppendLine();
        }
    }

    private static void AppendSituationTables(StringBuilder sb, IReadOnlyList<ProbeRecord> records)
    {
        sb.AppendLine("## Лидеры по сценариям");
        sb.AppendLine();
        sb.AppendLine("Лидер и его вероятность; для Jev — средняя по повторам.");
        sb.AppendLine();

        var sources = records.Where(r => r.IsOk).Select(r => (r.Model, r.Variant)).Distinct()
            .OrderBy(m => m.Model, StringComparer.Ordinal).ThenBy(m => m.Variant, StringComparer.Ordinal).ToList();
        foreach (var situation in ScenarioCatalog.Situations)
        {
            sb.AppendLine(Inv, $"### {situation.Key} — {situation.Summary}");
            sb.AppendLine();
            sb.AppendLine(Inv, $"| Архетип | Autonomous | {string.Join(" | ", sources.Select(s => $"{ShortModel(s.Model)} {s.Variant}"))} |");
            sb.AppendLine(Inv, $"|---|---|{string.Concat(Enumerable.Repeat("---|", sources.Count))}");
            foreach (var archetype in ScenarioCatalog.Archetypes)
            {
                var scenario = new Scenario(archetype, situation);
                var auto = AutonomousBaseline.Distribution(scenario);
                var cells = sources.Select(s =>
                {
                    var runs = records.Where(r => r.IsOk && r.Model == s.Model && r.Variant == s.Variant
                        && r.Archetype == archetype.Key && r.Situation == situation.Key).ToList();
                    return runs.Count == 0 ? "—" : LeaderCell(MeanDistribution(runs));
                });
                sb.AppendLine(Inv, $"| {archetype.Key} | {LeaderCell(auto)} | {string.Join(" | ", cells)} |");
            }

            sb.AppendLine();
        }
    }

    private static void AppendErrors(StringBuilder sb, IReadOnlyList<ProbeRecord> records)
    {
        var errors = records.Where(r => !r.IsOk).ToList();
        if (errors.Count == 0)
        {
            return;
        }

        sb.AppendLine("## Ошибки");
        sb.AppendLine();
        foreach (var group in errors.GroupBy(r => (r.Model, r.Outcome, r.HttpStatus, r.Error)).Take(20))
        {
            var text = (group.Key.Error ?? string.Empty).ReplaceLineEndings(" ");
            text = text.Length > 300 ? text[..300] + "…" : text;
            sb.AppendLine(Inv, $"- `{group.Key.Model}` {group.Key.Outcome} {group.Key.HttpStatus} × {group.Count()}: `{text.Replace('`', '\'')}`");
        }

        sb.AppendLine();
    }

    public static Dictionary<string, double> Distribution(ProbeRecord record) =>
        record.Probabilities is { Count: > 0 } p
            ? new Dictionary<string, double>(p, StringComparer.Ordinal)
            : new Dictionary<string, double>(StringComparer.Ordinal) { [record.Choice!] = 1.0 };

    private static Dictionary<string, double> MeanDistribution(IReadOnlyList<ProbeRecord> runs)
    {
        var sum = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, value) in runs.SelectMany(Distribution))
        {
            sum[key] = sum.GetValueOrDefault(key) + value / runs.Count;
        }

        return sum;
    }

    private static (double Spread, double Leaders) Spread<T>(IReadOnlyDictionary<string, T> distributions)
        where T : IReadOnlyDictionary<string, double>
    {
        double spreadSum = 0, leaderSum = 0;
        var counted = 0;
        foreach (var situation in ScenarioCatalog.Situations)
        {
            var dists = new List<IReadOnlyDictionary<string, double>>();
            foreach (var archetype in ScenarioCatalog.Archetypes)
            {
                if (distributions.TryGetValue($"{archetype.Key}/{situation.Key}", out var distribution))
                {
                    dists.Add(distribution);
                }
            }

            if (dists.Count < 2)
            {
                continue;
            }

            var keys = situation.Options.Select(o => o.Key).ToList();
            double pairSum = 0;
            var pairs = 0;
            for (var i = 0; i < dists.Count; i++)
            {
                for (var j = i + 1; j < dists.Count; j++)
                {
                    pairSum += JensenShannon(dists[i], dists[j], keys);
                    pairs++;
                }
            }

            spreadSum += pairSum / pairs;
            leaderSum += dists.Select(Leader).Distinct().Count();
            counted++;
        }

        return counted == 0 ? (double.NaN, double.NaN) : (spreadSum / counted, leaderSum / counted);
    }

    private static double JensenShannon(IReadOnlyDictionary<string, double> p, IReadOnlyDictionary<string, double> q, IReadOnlyList<string> keys)
    {
        double result = 0;
        foreach (var key in keys)
        {
            var a = p.GetValueOrDefault(key);
            var b = q.GetValueOrDefault(key);
            var m = (a + b) / 2;
            result += Kl(a, m) + Kl(b, m);
        }

        return result / 2;

        static double Kl(double x, double m) => x <= 0 ? 0 : x * Math.Log2(x / m);
    }

    private static string Leader(IReadOnlyDictionary<string, double> distribution) =>
        distribution.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;

    private static string LeaderCell(IReadOnlyDictionary<string, double> distribution)
    {
        var leader = Leader(distribution);
        return string.Create(Inv, $"{leader} {distribution[leader]:0.00}");
    }

    private static string ShortModel(string model) => model[(model.LastIndexOf('/') + 1)..];

    private static double Percentile(IReadOnlyList<double> sorted, int percentile)
    {
        if (sorted.Count == 0)
        {
            return double.NaN;
        }

        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    private static string Ms(double value) => double.IsNaN(value) ? "—" : value.ToString("0", Inv);

    private static string Pct(int part, int total) =>
        total == 0 ? "—" : (100.0 * part / total).ToString("0.#", Inv) + " %";
}
