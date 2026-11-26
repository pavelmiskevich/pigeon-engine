namespace Pigeon.Core;

/// <summary>Неизменяемый снимок <see cref="PigeonState"/> для сохранения и восстановления (ADR-0017).</summary>
/// <param name="IntentPlace">Место цели намерения, запомненное при его начале (#33).</param>
/// <param name="AbsenceSeconds">Сколько секунд приложение не работало за всю жизнь голубя.</param>
/// <param name="Memory">Память мест по ключу (ordinal); <c>null</c> — пусто.</param>
/// <param name="LifeStory">Хроника в порядке добавления; <c>null</c> — пусто.</param>
/// <remarks>
/// Снимки равны по содержимому: память и хроника сравниваются поэлементно, <c>null</c> равен
/// пустому списку — иначе снимок, прочитанный из базы, не был бы равен записанному.
/// </remarks>
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
    IReadOnlyList<LifeEvent>? LifeStory = null)
{
    public bool Equals(PigeonStateSnapshot other) =>
        Personality == other.Personality
        && Needs == other.Needs
        && Mood == other.Mood
        && Intent == other.Intent
        && IntentStartedTick == other.IntentStartedTick
        && IntentTarget == other.IntentTarget
        && IntentEndsTick == other.IntentEndsTick
        && IntentPlace == other.IntentPlace
        && AbsenceSeconds == other.AbsenceSeconds
        && (Memory ?? []).SequenceEqual(other.Memory ?? [])
        && (LifeStory ?? []).SequenceEqual(other.LifeStory ?? []);

    public override int GetHashCode() =>
        HashCode.Combine(Personality, Needs, Mood, Intent, IntentStartedTick, IntentTarget, IntentEndsTick, HashCode.Combine(IntentPlace, AbsenceSeconds, Memory?.Count ?? 0, LifeStory?.Count ?? 0));
}
