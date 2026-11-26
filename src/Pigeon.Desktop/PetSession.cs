using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite;
using Pigeon.Simulation;

namespace Pigeon.Desktop;

/// <summary>Как часто сохранять голубя и проверять резервные копии.</summary>
/// <param name="SaveEvery">Снимок в фоне раз в столько (ТЗ §31: «каждые N минут и при выходе»).</param>
/// <param name="BackupCheckEvery">Как часто проверять, не пора ли сделать суточную резервную копию.</param>
/// <param name="CloseTimeout">Сколько ждать последнего сохранения при выходе.</param>
internal sealed record PetSessionOptions(
    TimeSpan SaveEvery,
    TimeSpan BackupCheckEvery,
    TimeSpan CloseTimeout,
    PetWriterOptions? Writer = null)
{
    /// <summary>Авария теряет не больше минуты жизни голубя.</summary>
    public static PetSessionOptions Default { get; } = new(TimeSpan.FromMinutes(1), TimeSpan.FromHours(1), TimeSpan.FromSeconds(10));
}

/// <summary>
/// Голубь в хранилище (#33, ADR-0017): база <c>pigeon.db</c> и резервные копии в каталоге данных.
/// При первом запуске голубь рождается и записывается, при следующих — восстанавливается из
/// последнего снимка, а время, пока приложение не работало, досчитывается входом
/// <see cref="AbsenceEnded"/> (ТЗ §6.3).
/// </summary>
/// <remarks>
/// Все методы вызываются из потока хоста; запись идёт в фоне (<see cref="BackgroundPetWriter"/>),
/// резервные копии — в пуле потоков. Ошибки хранения не останавливают голубя: они уходят в
/// журнал хоста, а запись повторяется сама.
/// </remarks>
internal sealed class PetSession
{
    public const string DatabaseName = "pigeon.db";
    public const string BackupsName = "backups";

    /// <summary>Имя, пока выбора имени нет: <see cref="PetIdentity"/> требует непустое.</summary>
    public const string DefaultName = "Голубь";

    private readonly SqlitePetStore _store;
    private readonly BackgroundPetWriter _writer;
    private readonly SqliteBackupRotator _rotator;
    private readonly HostLog _log;
    private readonly TimeProvider _clock;
    private readonly PetSessionOptions _options;
    private long _enqueuedLength;
    private int _storyLength;
    private DateTimeOffset _lastSave;
    private DateTimeOffset _lastBackupCheck;
    private Task _backup = Task.CompletedTask;
    private bool _closed;

    private PetSession(
        SqlitePetStore store,
        string backups,
        PetIdentity identity,
        PigeonSimulation simulation,
        TimeSpan? absence,
        HostLog log,
        TimeProvider clock,
        PetSessionOptions options)
    {
        _store = store;
        _rotator = new SqliteBackupRotator(store.DatabasePath, backups);
        _log = log;
        _clock = clock;
        _options = options;
        Identity = identity;
        Simulation = simulation;
        Absence = absence;
        _enqueuedLength = simulation.JournalLength;

        // Записи, с которыми голубь родился или восстановлен, уже были: в журнал пишутся только новые.
        _storyLength = simulation.LifeStory.Count;

        _writer = new BackgroundPetWriter(store, identity.Id, options.Writer);
        _writer.Failed += (_, failure) => ReportWriteFailure(failure);

        var now = clock.GetUtcNow();
        _lastSave = now;
        _lastBackupCheck = now;
        StartBackupCheck(now);
    }

    public PetIdentity Identity { get; }

    public PigeonSimulation Simulation { get; }

    /// <summary>Сколько приложение не работало до этого запуска; <c>null</c> — голубь только родился или времени сохранения нет.</summary>
    public TimeSpan? Absence { get; }

    public string DatabasePath => _store.DatabasePath;

    /// <summary>Открывает базу, рождает или восстанавливает голубя.</summary>
    /// <exception cref="PetStoreCorruptedException">База испорчена, а резервной копии нет.</exception>
    public static async Task<PetSession> OpenAsync(
        string dataDirectory,
        HostLog log,
        TimeProvider clock,
        PetSessionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(clock);
        options ??= PetSessionOptions.Default;

        Directory.CreateDirectory(dataDirectory);
        var backups = Path.Combine(dataDirectory, BackupsName);
        var store = await SqlitePetStore.OpenAsync(
            Path.Combine(dataDirectory, DatabaseName),
            new StoreOpenOptions(backups, Clock: clock),
            cancellationToken).ConfigureAwait(false);
        ReportOpening(store.OpenReport, log);

        var pets = await store.LoadPetsAsync(cancellationToken).ConfigureAwait(false);
        if (pets.Count > 1)
        {
            log.Warn($"В базе {pets.Count} питомцев; живёт старший.");
        }

        var now = clock.GetUtcNow();
        if (pets.Count == 0)
        {
            var seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
            var born = PetIdentity.Create(new PetId(Guid.NewGuid()), DefaultName, now, seed, PlumageVariant.Black);
            await store.SavePetAsync(born, cancellationToken).ConfigureAwait(false);
            log.Info("Родился новый голубь.");

            var session = new PetSession(store, backups, born, new PigeonSimulation(seed, SimulationOptions.Desktop), null, log, clock, options);
            session.SaveNow();
            return session;
        }

        var identity = pets[0];
        var snapshot = await store.LoadLatestAsync(identity.Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            // Записан при рождении, но первый снимок не успел: жизнь начинается заново с того же seed.
            log.Warn("У голубя нет ни одного снимка — он начинает жизнь с рождения.");
            var fresh = new PetSession(store, backups, identity, new PigeonSimulation(identity.Seed, SimulationOptions.Desktop), null, log, clock, options);
            fresh.SaveNow();
            return fresh;
        }

        if (snapshot.Seed != identity.Seed)
        {
            throw new InvalidDataException("Снимок голубя не соответствует его seed: база несогласована.");
        }

        var simulation = PigeonSimulation.Restore(snapshot);
        TimeSpan? absence = null;
        if (await store.GetLastSavedAtAsync(identity.Id, cancellationToken).ConfigureAwait(false) is { } savedAt)
        {
            // Часы могли уйти назад (смена часового пояса не влияет — время в UTC, но ручная правка — да).
            absence = now > savedAt ? now - savedAt : TimeSpan.Zero;
            simulation.Submit(new AbsenceEnded(absence.Value));
        }

        log.Info(absence is { } away
            ? $"Голубь восстановлен: тик {snapshot.Tick}, приложение не работало {Describe(away)}."
            : $"Голубь восстановлен: тик {snapshot.Tick}.");
        return new PetSession(store, backups, identity, simulation, absence, log, clock, options);
    }

    /// <summary>
    /// Вызывается каждый проход цикла хоста: отпускает подтверждённый базой журнал (#50), пишет в
    /// журнал хоста новые записи хроники и, когда пора, ставит снимок на запись и проверяет
    /// резервную копию.
    /// </summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_closed, this);

        Simulation.ForgetJournalBefore(Math.Min(_writer.ConfirmedLength, Simulation.JournalLength));

        if (Simulation.LifeStory.Count > _storyLength)
        {
            ReportLifeEvents();
        }

        var now = _clock.GetUtcNow();
        if (now - _lastSave >= _options.SaveEvery || now < _lastSave)
        {
            SaveNow();
        }

        if (now - _lastBackupCheck >= _options.BackupCheckEvery || now < _lastBackupCheck)
        {
            _lastBackupCheck = now;
            StartBackupCheck(now);
        }
    }

    /// <summary>Ставит текущий снимок и новые записи журнала в очередь фоновой записи.</summary>
    public void SaveNow()
    {
        ObjectDisposedException.ThrowIf(_closed, this);

        _writer.Enqueue(Simulation.CreateSnapshot(), Simulation.GetJournalFrom(_enqueuedLength));
        _enqueuedLength = Simulation.JournalLength;
        _lastSave = _clock.GetUtcNow();
    }

    /// <summary>Ждёт, пока запишется всё поставленное в очередь (для тестов и выхода).</summary>
    public Task FlushAsync() => _writer.FlushAsync();

    /// <summary>
    /// Завершает запись. <paramref name="saveFinal"/> — сохранить текущее состояние; после падения
    /// симуляции его не сохраняют: в базе остаётся последний согласованный снимок.
    /// </summary>
    public async Task CloseAsync(bool saveFinal)
    {
        if (_closed)
        {
            return;
        }

        if (saveFinal)
        {
            SaveNow();
        }

        _closed = true;
        try
        {
            await _writer.DisposeAsync().AsTask().WaitAsync(_options.CloseTimeout).ConfigureAwait(false);
            if (saveFinal)
            {
                _log.Info($"Голубь сохранён: тик {Simulation.Tick}.");
            }
        }
        catch (TimeoutException)
        {
            _log.Error($"Последнее сохранение не успело за {_options.CloseTimeout.TotalSeconds:0} с.");
        }
        catch (InvalidOperationException error)
        {
            _log.Error("Последнее сохранение не удалось.", error);
        }

        try
        {
            await _backup.WaitAsync(_options.CloseTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _log.Warn("Резервная копия не успела завершиться до выхода.");
        }
    }

    /// <summary>
    /// Новые записи хроники — в журнал хоста и сразу на диск (#33): события редкие, а после аварии
    /// голубь не должен прожить «впервые» второй раз. В журнал пишется только вид события: в
    /// ключе места — идентификатор монитора.
    /// </summary>
    private void ReportLifeEvents()
    {
        var story = Simulation.LifeStory;
        for (var i = _storyLength; i < story.Count; i++)
        {
            _log.Info($"В хронике: {Phrase(story[i].Kind)}.");
        }

        _storyLength = story.Count;
        SaveNow();
    }

    internal static string Phrase(LifeEventKind kind) => kind switch
    {
        LifeEventKind.Born => "родился",
        LifeEventKind.FirstWalk => "впервые прогулялся",
        LifeEventKind.FirstInteraction => "впервые встретил внимание человека",
        LifeEventKind.FirstSleep => "впервые уснул",
        LifeEventKind.FirstAdventure => "впервые отправился исследовать",
        LifeEventKind.FirstMultiMonitorTransition => "впервые перелетел на другой монитор",
        LifeEventKind.FirstVisitToMonitor => "впервые побывал на этом мониторе",
        LifeEventKind.FirstFavoriteTerritory => "у него появился любимый монитор",
        _ => "новое событие",
    };

    private static string Describe(TimeSpan away) =>
        away < TimeSpan.FromMinutes(2) ? $"{Math.Round(away.TotalSeconds)} с"
        : away < TimeSpan.FromHours(48) ? $"{Math.Round(away.TotalMinutes)} мин"
        : $"{Math.Round(away.TotalDays)} сут";

    private static void ReportOpening(StoreOpenReport report, HostLog log)
    {
        if (report.Attempts > 1)
        {
            log.Warn($"База открылась с {report.Attempts}-й попытки; временные ошибки SQLite: {string.Join(", ", report.TransientErrors)}.");
        }

        if (report.RestoredFrom is { } backup)
        {
            log.Warn($"База была испорчена и восстановлена из резервной копии {Path.GetFileName(backup)}; испорченная отложена как {Path.GetFileName(report.QuarantinedTo)}.");
        }
    }

    /// <summary>Повторяющиеся неудачи пишутся на 1, 2, 4, 8… попытке: запись повторяется до 30 с, журнал не забивается.</summary>
    private void ReportWriteFailure(PetWriteFailure failure)
    {
        if (!failure.WillRetry)
        {
            _log.Error("Запись голубя остановлена: ошибка не пройдёт сама.", failure.Error);
        }
        else if ((failure.Attempt & (failure.Attempt - 1)) == 0)
        {
            _log.Warn($"Сохранение голубя не удалось (попытка {failure.Attempt}), запись повторится.", failure.Error);
        }
    }

    private void StartBackupCheck(DateTimeOffset now)
    {
        if (!_backup.IsCompleted)
        {
            return;
        }

        _backup = Task.Run(() =>
        {
            try
            {
                if (_rotator.BackupIfDue(now) is not null)
                {
                    _log.Info("Сделана суточная резервная копия базы.");
                }
            }
            catch (Exception error) when (error is IOException or SqliteException or UnauthorizedAccessException)
            {
                _log.Warn("Резервная копия базы не сделана.", error);
            }
        });
    }
}
