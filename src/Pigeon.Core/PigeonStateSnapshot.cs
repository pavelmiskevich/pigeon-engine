namespace Pigeon.Core;

/// <summary>Неизменяемый снимок <see cref="PigeonState"/> для сохранения и восстановления (ADR-0017).</summary>
public readonly record struct PigeonStateSnapshot(double Fatigue, IntentKind Intent, long IntentStartedTick);
