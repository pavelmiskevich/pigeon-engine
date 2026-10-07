using Pigeon.Simulation;

namespace Pigeon.Persistence.Sqlite.Serialization;

/// <summary>
/// Стабильные имена входов в хранилище. Имя — часть формата: переименование класса его не
/// меняет, а новое имя требует миграции.
/// </summary>
internal static class InputEventCodec
{
    public const string UserPoked = "user-poked";

    public static string Encode(InputEvent inputEvent) => inputEvent switch
    {
        Simulation.UserPoked => UserPoked,
        _ => throw new NotSupportedException($"Вход {inputEvent.GetType().Name} не поддерживается хранилищем."),
    };

    public static InputEvent Decode(string type) => type switch
    {
        UserPoked => new Simulation.UserPoked(),
        _ => throw new InvalidDataException($"Неизвестный тип входа в хранилище: «{type}»."),
    };
}
