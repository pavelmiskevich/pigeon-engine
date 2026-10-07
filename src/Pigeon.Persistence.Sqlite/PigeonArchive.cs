using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite.Serialization;
using Pigeon.Simulation;

namespace Pigeon.Persistence.Sqlite;

/// <summary>Содержимое файла <c>.pigeon</c>.</summary>
/// <param name="JournalTail">Записи журнала после снимка.</param>
public sealed record PigeonArchiveContent(PetId PetId, SimulationSnapshot Snapshot, IReadOnlyList<JournalEntry> JournalTail);

/// <summary>
/// Экспорт и импорт питомца одним файлом <c>.pigeon</c> (ТЗ §31, черновик формата): zip с
/// <c>manifest.json</c>, <c>snapshot.json</c> и <c>journal.json</c>. Life Story и Memory войдут в
/// формат, когда появятся в домене (MVP-1).
/// </summary>
public static class PigeonArchive
{
    public const string FileExtension = ".pigeon";
    public const string FormatName = "pigeon";
    public const int CurrentFormatVersion = 1;

    private const string ManifestEntry = "manifest.json";
    private const string SnapshotEntry = "snapshot.json";
    private const string JournalEntry = "journal.json";

    public static void Export(Stream destination, PigeonArchiveContent content)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(content);
        content.Snapshot.Validate();

        var manifest = new ArchiveManifestDto(
            FormatName,
            CurrentFormatVersion,
            SqliteSchemaMigrator.LatestVersion,
            content.Snapshot.FormatVersion,
            content.PetId.Value,
            content.Snapshot.Tick,
            content.Snapshot.JournalLength);

        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        Write(zip, ManifestEntry, JsonSerializer.Serialize(manifest, PersistenceJsonContext.Default.ArchiveManifestDto));
        Write(zip, SnapshotEntry, SnapshotSerializer.Serialize(content.Snapshot));
        Write(zip, JournalEntry, SnapshotSerializer.SerializeJournal(content.JournalTail));
    }

    public static PigeonArchiveContent Import(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var manifest = JsonSerializer.Deserialize(Read(zip, ManifestEntry), PersistenceJsonContext.Default.ArchiveManifestDto)
            ?? throw new InvalidDataException("Пустой manifest.json.");

        if (manifest.Format != FormatName)
        {
            throw new InvalidDataException($"Это не файл питомца: формат «{manifest.Format}».");
        }

        if (manifest.FormatVersion > CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"Файл питомца создан более новой версией приложения (формат {manifest.FormatVersion}).");
        }

        var snapshot = SnapshotSerializer.Deserialize(Read(zip, SnapshotEntry));
        snapshot.Validate();
        var journal = SnapshotSerializer.DeserializeJournal(Read(zip, JournalEntry));

        if (snapshot.Tick != manifest.Tick || snapshot.JournalLength != manifest.JournalLength)
        {
            throw new InvalidDataException("manifest.json не соответствует snapshot.json.");
        }

        return new PigeonArchiveContent(new PetId(manifest.PetId), snapshot, journal);
    }

    private static void Write(ZipArchive zip, string name, string json)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(json);
    }

    private static string Read(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"В файле питомца нет {name}.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
