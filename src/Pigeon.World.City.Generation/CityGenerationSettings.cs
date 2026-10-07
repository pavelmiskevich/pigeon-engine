namespace Pigeon.World.City.Generation;

/// <param name="BlocksX">Кварталов по горизонтали.</param>
/// <param name="BlocksY">Кварталов по вертикали.</param>
/// <param name="MaxFloors">Предельная этажность в центре района.</param>
/// <param name="ParkOneIn">Примерно каждый N-й квартал — сквер без зданий.</param>
public sealed record CityGenerationSettings(int BlocksX, int BlocksY, int MaxFloors, int ParkOneIn)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BlocksX, nameof(BlocksX));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BlocksY, nameof(BlocksY));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxFloors, nameof(MaxFloors));
        ArgumentOutOfRangeException.ThrowIfLessThan(ParkOneIn, 2, nameof(ParkOneIn));
    }
}
