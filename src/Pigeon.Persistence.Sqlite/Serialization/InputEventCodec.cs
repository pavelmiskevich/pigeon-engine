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
    public const string UserPetted = "user-petted";
    public const string UserPickedUp = "user-picked-up";
    public const string UserDropped = "user-dropped";
    public const string CursorApproached = "cursor-approached";

    /// <summary>Данные — длительность отсутствия в тиках <see cref="TimeSpan"/> (100 нс), десятичным числом.</summary>
    public const string AbsenceEnded = "absence-ended";

    /// <summary>Данные — число <c>DisplacementReason</c>.</summary>
    public const string FootingLost = "footing-lost";

    /// <summary>Данные — «намерение;тик начала;исход;причина» числами, причина пустая при успехе.</summary>
    public const string ActionFinished = "action-finished";

    /// <summary>Данные — JSON-массив affordances (формат снимка).</summary>
    public const string AffordancesObserved = "affordances-observed";

    public static EncodedInput Encode(InputEvent inputEvent) => inputEvent switch
    {
        Simulation.UserPoked => new(UserPoked, null),
        Simulation.UserPetted => new(UserPetted, null),
        Simulation.UserPickedUp => new(UserPickedUp, null),
        Simulation.UserDropped => new(UserDropped, null),
        Simulation.CursorApproached => new(CursorApproached, null),
        Simulation.AbsenceEnded absence => new(AbsenceEnded, absence.Duration.Ticks.ToString(CultureInfo.InvariantCulture)),
        Simulation.FootingLost lost => new(FootingLost, ((int)lost.Reason).ToString(CultureInfo.InvariantCulture)),
        Simulation.ActionFinished finished => new(ActionFinished, string.Create(CultureInfo.InvariantCulture,
            $"{(int)finished.Intent};{finished.IntentStartedTick};{(int)finished.Outcome};{(finished.Failure is { } f ? (int)f : string.Empty)}")),
        Simulation.AffordancesObserved observed => new(AffordancesObserved, SnapshotSerializer.SerializeAffordances(observed.Affordances)),
        _ => throw new NotSupportedException($"Вход {inputEvent.GetType().Name} не поддерживается хранилищем."),
    };

    public static InputEvent Decode(string type, string? payload) => type switch
    {
        UserPoked => new Simulation.UserPoked(),
        UserPetted => new Simulation.UserPetted(),
        UserPickedUp => new Simulation.UserPickedUp(),
        UserDropped => new Simulation.UserDropped(),
        CursorApproached => new Simulation.CursorApproached(),
        AbsenceEnded => new Simulation.AbsenceEnded(TimeSpan.FromTicks(ParseNumber(type, payload))),
        FootingLost => new Simulation.FootingLost((World.Abstractions.DisplacementReason)(int)ParseNumber(type, payload)),
        ActionFinished => ParseActionFinished(type, payload),
        AffordancesObserved => new Simulation.AffordancesObserved(SnapshotSerializer.DeserializeAffordances(
            payload ?? throw new InvalidDataException($"У входа «{type}» нет данных."))),
        _ => throw new InvalidDataException($"Неизвестный тип входа в хранилище: «{type}»."),
    };

    private static Simulation.ActionFinished ParseActionFinished(string type, string? payload)
    {
        var parts = payload?.Split(';') ?? [];
        if (parts.Length != 4)
        {
            throw new InvalidDataException($"Повреждённые данные входа «{type}»: «{payload}».");
        }

        return new Simulation.ActionFinished(
            (Core.IntentKind)(int)ParseNumber(type, parts[0]),
            ParseNumber(type, parts[1]),
            (World.Abstractions.ActionOutcome)(int)ParseNumber(type, parts[2]),
            parts[3].Length == 0 ? null : (World.Abstractions.ActionFailure)(int)ParseNumber(type, parts[3]));
    }

    private static long ParseNumber(string type, string? payload) =>
        long.TryParse(payload, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            ? ticks
            : throw new InvalidDataException($"Повреждённые данные входа «{type}»: «{payload}».");
}
