namespace Pigeon.Core;

/// <summary>Неизменяемый снимок <see cref="PigeonState"/> для сохранения и восстановления (ADR-0017).</summary>
public readonly record struct PigeonStateSnapshot(
    Personality Personality,
    Needs Needs,
    Mood Mood,
    IntentKind Intent,
    long IntentStartedTick,
    AffordanceId? IntentTarget = null,
    long IntentEndsTick = 0);
