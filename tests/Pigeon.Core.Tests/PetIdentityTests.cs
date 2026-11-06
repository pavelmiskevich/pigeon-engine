namespace Pigeon.Core.Tests;

/// <summary>Identity питомца (ТЗ §5.1).</summary>
public sealed class PetIdentityTests
{
    private static readonly PetId Id = new(Guid.Parse("3c9e1f20-7a4b-4d6e-9f81-2b5c7d9e0a13"));

    [Fact]
    public void Create_NormalizesNameAndBirthTime()
    {
        var bornAt = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(3));

        var identity = PetIdentity.Create(Id, "  Гоша ", bornAt, 0xC0FFEE, PlumageVariant.Black);

        Assert.Equal("Гоша", identity.Name);
        Assert.Equal(TimeSpan.Zero, identity.BornAt.Offset);
        Assert.Equal(bornAt, identity.BornAt);
        Assert.Equal(PetIdentity.CurrentSchemaVersion, identity.SchemaVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Голубь, у которого слишком длинное имя для окна")]
    public void Create_RejectsBadNames(string name)
    {
        Assert.Throws<ArgumentException>(() => PetIdentity.Create(Id, name, DateTimeOffset.UnixEpoch, 1, PlumageVariant.Black));
    }

    [Fact]
    public void Validate_RejectsMissingIdUnknownPlumageAndOtherSchema()
    {
        var identity = PetIdentity.Create(Id, "Гоша", DateTimeOffset.UnixEpoch, 1, PlumageVariant.Black);

        Assert.Throws<ArgumentException>(() => (identity with { Id = new PetId(Guid.Empty) }).Validate());
        Assert.Throws<ArgumentException>(() => (identity with { Plumage = (PlumageVariant)7 }).Validate());
        Assert.Throws<NotSupportedException>(() => (identity with { SchemaVersion = 2 }).Validate());
    }
}
