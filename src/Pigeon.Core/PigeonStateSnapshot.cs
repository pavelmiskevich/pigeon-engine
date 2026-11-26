namespace Pigeon.Core;

/// <summary>Неизменяемый снимок <see cref="PigeonState"/> для сохранения и восстановления (ADR-0017).</summary>
/// <param name="IntentPlace">Место цели намерения, запомненное при его начале (#33).</param>
/// <param name="AbsenceSeconds">Сколько секунд приложение не работало за всю жизнь голубя.</param>
/// <param name="Memory">Память мест по ключу (ordinal); <c>null</c> — пусто.</param>
/// <param name="LifeStory">Хроника в порядке добавления; <c>null</c> — пусто.</param>
public readonly record struct PigeonStateSnapshot(
    Personality Personality,
    Needs Needs,
    Mood Mood,
    IntentKind Intent,
    long IntentStartedTick,
    AffordanceId? IntentTarget = null,
    long IntentEndsTick = 0,
    PlaceKey? IntentPlace = null,
    long AbsenceSeconds = 0,
    IReadOnlyList<PlaceMemoryEntry>? Memory = null,
    IReadOnlyList<LifeEvent>? LifeStory = null);
