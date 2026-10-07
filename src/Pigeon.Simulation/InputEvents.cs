namespace Pigeon.Simulation;

/// <summary>
/// Внешний вход симуляции: действие пользователя или событие мира. Входы — единственное, что
/// влияет на симуляцию снаружи, поэтому их журнал вместе с seed воспроизводит её (ТЗ §3.3).
/// </summary>
public abstract record InputEvent;

/// <summary>Пользователь ткнул голубя: отдых прерывается, голубь немного вздрагивает.</summary>
public sealed record UserPoked : InputEvent;

/// <summary>Вход, применённый в конкретном тике.</summary>
public readonly record struct JournalEntry(long Tick, InputEvent Event);
