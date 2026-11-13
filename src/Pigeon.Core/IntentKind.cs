namespace Pigeon.Core;

/// <summary>
/// Намерение голубя (ТЗ §8.1): <i>что</i> он хочет, без того, <i>как</i> это исполняет мир.
/// В MVP-1 — одиннадцать намерений; остальные из каталога ТЗ добавятся вместе с нужными мирами.
/// Числа — часть формата снимка: существующие не меняются, новые добавляются в конец.
/// </summary>
public enum IntentKind
{
    Wander = 0,
    Rest = 1,
    Sleep = 2,
    Explore = 3,
    Observe = 4,
    SeekAttention = 5,
    FollowTarget = 6,
    Play = 7,
    Groom = 8,
    Relocate = 9,

    /// <summary>Отойти от того, что пугает или раздражает: курсора, опасности (M1.6).</summary>
    Flee = 10,
}
