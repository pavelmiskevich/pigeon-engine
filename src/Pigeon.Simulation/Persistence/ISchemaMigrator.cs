namespace Pigeon.Simulation.Persistence;

/// <summary>
/// Миграции схемы хранилища: только вперёд (ADR-0017). Хранилище более новой версии, чем
/// знает приложение, не открывается — откатывать данные нельзя.
/// </summary>
public interface ISchemaMigrator
{
    int CurrentVersion { get; }

    ValueTask<int> GetVersionAsync(CancellationToken cancellationToken);

    ValueTask MigrateAsync(CancellationToken cancellationToken);
}
