using System.Globalization;
using Pigeon.Simulation;

namespace Pigeon.Persistence.Sqlite.Serialization;

/// <summary>Вход в формате хранения: стабильное имя типа и данные, если они у входа есть.</summary>
internal readonly record struct EncodedInput(string Type, string? Payload);

/// <summary>
/// Стабильные имена и данные входов в хранилище. Имя — часть формата: переименование класса его
/// не меняет, а новое имя требует миграции.
/// </summary>
internal static class InputEventCodec
{
    public const string UserPoked = "user-poked";

    /// <summary>Данные — длительность отсутствия в тиках <see cref="TimeSpan"/> (100 нс), десятичным числом.</summary>
    public const string AbsenceEnded = "absence-ended";

    public static EncodedInput Encode(InputEvent inputEvent) => inputEvent switch
    {
        Simulation.UserPoked => new(UserPoked, null),
        Simulation.AbsenceEnded absence => new(AbsenceEnded, absence.Duration.Ticks.ToString(CultureInfo.InvariantCulture)),
        _ => throw new NotSupportedException($"Вход {inputEvent.GetType().Name} не поддерживается хранилищем."),
    };

    public static InputEvent Decode(string type, string? payload) => type switch
    {
        UserPoked => new Simulation.UserPoked(),
        AbsenceEnded => new Simulation.AbsenceEnded(TimeSpan.FromTicks(ParseTicks(type, payload))),
        _ => throw new InvalidDataException($"Неизвестный тип входа в хранилище: «{type}»."),
    };

    private static long ParseTicks(string type, string? payload) =>
        long.TryParse(payload, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            ? ticks
            : throw new InvalidDataException($"Повреждённые данные входа «{type}»: «{payload}».");
}
