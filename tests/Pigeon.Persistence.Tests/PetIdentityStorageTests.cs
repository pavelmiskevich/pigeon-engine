using Pigeon.Core;
using Pigeon.Persistence.Sqlite;

namespace Pigeon.Persistence.Tests;

/// <summary>Неизменная часть питомца и время сохранения снимка в базе (схема v4, #33).</summary>
public sealed class PetIdentityStorageTests
{
    private static readonly DateTimeOffset Born = new(2026, 11, 26, 9, 30, 15, 123, TimeSpan.Zero);

    [Fact]
    public async Task Pet_RoundTrips_IncludingSeedAboveLongMax()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var pet = PetIdentity.Create(new PetId(Guid.NewGuid()), "Гоша", Born.AddTicks(7), ulong.MaxValue - 5, PlumageVariant.Black);

        await store.SavePetAsync(pet, TestContext.Current.CancellationToken);

        Assert.Equal(pet, Assert.Single(await store.LoadPetsAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task SavingTheSamePetTwice_ChangesNothing_ButADifferentPetWithTheSameIdIsRefused()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var pet = PetIdentity.Create(new PetId(Guid.NewGuid()), "Гоша", Born, 42, PlumageVariant.Black);

        await store.SavePetAsync(pet, TestContext.Current.CancellationToken);
        await store.SavePetAsync(pet, TestContext.Current.CancellationToken);

        Assert.Single(await store.LoadPetsAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SavePetAsync(pet with { Seed = 43 }, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Pets_AreListedOldestFirst()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var younger = PetIdentity.Create(new PetId(Guid.NewGuid()), "Младший", Born.AddDays(1), 2, PlumageVariant.Black);
        var older = PetIdentity.Create(new PetId(Guid.NewGuid()), "Старший", Born, 1, PlumageVariant.Black);

        await store.SavePetAsync(younger, TestContext.Current.CancellationToken);
        await store.SavePetAsync(older, TestContext.Current.CancellationToken);

        Assert.Equal(["Старший", "Младший"], (await store.LoadPetsAsync(TestContext.Current.CancellationToken)).Select(p => p.Name));
    }

    [Fact]
    public async Task LastSavedAt_IsTheWallClockOfTheLatestSnapshot()
    {
        using var db = new TestDatabase();
        var clock = new ManualClock(Born);
        var store = await SqlitePetStore.OpenAsync(db.Path, new StoreOpenOptions(Clock: clock), TestContext.Current.CancellationToken);
        var pet = new PetId(Guid.NewGuid());
        var simulation = TestDatabase.RunSimulation(seed: 7, ticks: 20);

        Assert.Null(await store.GetLastSavedAtAsync(pet, TestContext.Current.CancellationToken));

        await store.SaveAsync(pet, simulation.CreateSnapshot(), simulation.Journal, TestContext.Current.CancellationToken);
        clock.Now = Born.AddHours(3);
        await store.SaveAsync(pet, simulation.CreateSnapshot(), [], TestContext.Current.CancellationToken);

        Assert.Equal(Born.AddHours(3), await store.GetLastSavedAtAsync(pet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VersionOneDatabase_GetsPetsTable_AndOldSnapshotsHaveNoSaveTime()
    {
        using var db = new TestDatabase();
        TestDatabase.CreateFromFixture(db.Path, "schema-v1.sql");

        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);

        Assert.Empty(await store.LoadPetsAsync(TestContext.Current.CancellationToken));
        Assert.Equal("0", TestDatabase.Query(db.Path, "SELECT COUNT(saved_at) FROM snapshots;"));
        Assert.Null(await store.GetLastSavedAtAsync(new PetId(Guid.Parse("6f1c2a5e-0b7d-4c1e-9a3b-2d4e6f8a0c11")), TestContext.Current.CancellationToken));
    }
}

/// <summary>Настенные часы, которые идут только по команде теста.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
