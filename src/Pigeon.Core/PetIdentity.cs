namespace Pigeon.Core;

/// <summary>Окрас голубя (ТЗ §5.1). Владелец выбрал реалистичного чёрного сизаря; другие окрасы — позже.</summary>
public enum PlumageVariant
{
    Black = 0,
}

/// <summary>
/// Неизменная часть питомца (ТЗ §5.1): не меняется при смене AI-провайдера, мира и хранилища.
/// Создаётся хостом при рождении питомца; <see cref="Seed"/> — корневой seed его симуляции.
/// </summary>
/// <param name="BornAt">Момент рождения по часам хоста, UTC. В симуляции не участвует.</param>
/// <param name="SchemaVersion">Версия формата Identity; растёт при несовместимом изменении полей.</param>
public sealed record PetIdentity(
    PetId Id,
    string Name,
    DateTimeOffset BornAt,
    ulong Seed,
    PlumageVariant Plumage,
    int SchemaVersion)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxNameLength = 40;

    public static PetIdentity Create(PetId id, string name, DateTimeOffset bornAt, ulong seed, PlumageVariant plumage)
    {
        var identity = new PetIdentity(id, name?.Trim() ?? string.Empty, bornAt.ToUniversalTime(), seed, plumage, CurrentSchemaVersion);
        identity.Validate();
        return identity;
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Версия Identity {SchemaVersion} не поддерживается; ожидается {CurrentSchemaVersion}.");
        }

        if (Id.Value == Guid.Empty)
        {
            throw new ArgumentException("У питомца нет идентификатора.");
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength || Name != Name.Trim())
        {
            throw new ArgumentException($"Имя питомца пустое, длиннее {MaxNameLength} символов или с пробелами по краям.");
        }

        if (BornAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Момент рождения хранится в UTC.");
        }

        if (!Enum.IsDefined(Plumage))
        {
            throw new ArgumentException($"Неизвестный окрас: {(int)Plumage}.");
        }
    }
}
