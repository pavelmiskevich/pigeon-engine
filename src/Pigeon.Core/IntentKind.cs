namespace Pigeon.Core;

/// <summary>
/// Намерение голубя (ТЗ §8.1). В MVP-0 — только два, чтобы проверить цикл симуляции;
/// каталог намерений MVP-1 расширяет этот список.
/// </summary>
public enum IntentKind
{
    Wander = 0,
    Rest = 1,
}
