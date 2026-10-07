namespace Pigeon.Core;

/// <summary>
/// Стабильный идентификатор питомца (ТЗ §5.1): не меняется при смене AI-провайдера, мира и
/// хранилища. Создаётся хостом при рождении питомца.
/// </summary>
public readonly record struct PetId(Guid Value)
{
    public override string ToString() => Value.ToString("D");
}
