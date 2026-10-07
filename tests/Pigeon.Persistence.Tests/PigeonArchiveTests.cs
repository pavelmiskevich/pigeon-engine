using System.IO.Compression;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite;
using Pigeon.Simulation;

namespace Pigeon.Persistence.Tests;

public sealed class PigeonArchiveTests
{
    private static readonly PetId Pet = new(Guid.Parse("9d3f5b71-2a4c-4e6b-8d0f-1b3d5f7a9c22"));

    [Fact]
    public void ExportedPet_ImportsAndContinuesExactlyTheSame()
    {
        var original = TestDatabase.RunSimulation(0x5EED_00A1UL, 3_000);
        var snapshot = original.CreateSnapshot();
        JournalEntry[] tail = [new(snapshot.Tick + 5, new UserPoked())];
        using var file = new MemoryStream();

        PigeonArchive.Export(file, new PigeonArchiveContent(Pet, snapshot, tail));
        file.Position = 0;
        var imported = PigeonArchive.Import(file);

        Assert.Equal(Pet, imported.PetId);
        Assert.Equal(tail, imported.JournalTail);
        var restored = PigeonSimulation.Restore(imported.Snapshot);
        original.RunTicks(2_000);
        restored.RunTicks(2_000);
        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void Archive_HasManifestSnapshotAndJournal()
    {
        var snapshot = TestDatabase.RunSimulation(1, 100).CreateSnapshot();
        using var file = new MemoryStream();

        PigeonArchive.Export(file, new PigeonArchiveContent(Pet, snapshot, []));
        file.Position = 0;
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        Assert.Equal(["journal.json", "manifest.json", "snapshot.json"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ArchiveFromANewerVersion_IsRefused()
    {
        using var file = new MemoryStream();
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write("""{"format":"pigeon","formatVersion":99,"schemaVersion":1,"snapshotFormatVersion":1,"petId":"9d3f5b71-2a4c-4e6b-8d0f-1b3d5f7a9c22","tick":0,"journalLength":0}""");
        }

        file.Position = 0;

        Assert.Throws<NotSupportedException>(() => PigeonArchive.Import(file));
    }
}
