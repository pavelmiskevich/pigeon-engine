using Pigeon.Common.Hashing;
using Pigeon.Core;
using Pigeon.World.Abstractions;
using Pigeon.World.Desktop.Capabilities;

namespace Pigeon.World.Desktop.Screen;

/// <summary>Что изменилось на экране (ТЗ §13). Для журнала голубя важен только <see cref="PigeonDisplaced"/>.</summary>
public abstract record DesktopWorldEvent;

public sealed record MonitorConnected(MonitorState Monitor) : DesktopWorldEvent;

public sealed record MonitorDisconnected(MonitorState Monitor) : DesktopWorldEvent;

/// <summary>Сменились границы, рабочая область, масштаб (DPI) или основной монитор.</summary>
public sealed record MonitorChanged(MonitorState Before, MonitorState After) : DesktopWorldEvent
{
    public bool ScaleChanged => Before.Scale != After.Scale;
}

/// <summary>Возможность пропала или появилась на лету (ТЗ §2.3).</summary>
public sealed record CapabilityChanged(Capability Capability, bool Usable) : DesktopWorldEvent;

public sealed record LevelChanged(DesktopLevel From, DesktopLevel To) : DesktopWorldEvent;

/// <summary>Голубь перебрался на другой монитор, закончив перелёт (ТЗ §12).</summary>
public sealed record PigeonRelocated(MonitorKey From, MonitorKey To) : DesktopWorldEvent;

/// <summary>
/// Голубь лишился опоры не по своей воле (ТЗ §12): его монитор отключили или мир потерял
/// возможность. Хост подаёт это в симуляцию входом, чтобы голубь испугался, а не исчез.
/// </summary>
/// <param name="To">Где голубь окажется; <c>null</c> — поверхностей нет, остаётся режим окна (L1).</param>
public sealed record PigeonDisplaced(MonitorKey? From, MonitorKey? To, DisplacementReason Reason) : DesktopWorldEvent;

/// <summary>
/// Desktop World уровня L2 (ТЗ §11, §32; матрица возможностей §4): поверхности экрана и мониторы
/// как affordances. Окна и курсор — уровень L3, позже.
/// </summary>
/// <remarks>
/// <para>
/// Мир хранит, на каком мониторе голубь. Предлагаются поверхности этого монитора и другие
/// мониторы как «другая территория» (<see cref="AffordanceKind.TerritoryZone"/>): поверхности
/// чужого монитора доступны только после перелёта (ТЗ §12 — мониторы отдельные территории).
/// </para>
/// <para>
/// Изменения экрана мир узнаёт опросом <see cref="Refresh"/> — хост вызывает его раз в
/// секунду-две и по системным уведомлениям. Разница снимков превращается в события.
/// </para>
/// </remarks>
public sealed class DesktopWorld : IAffordanceSource
{
    /// <summary>Оценки поверхностей: пол безопасен, панель задач приятнее, край экрана — высоко и рискованно.</summary>
    internal static readonly (double Attractiveness, double Risk, TimeSpan Travel) FloorSpot = (0.4, 0.0, TimeSpan.FromSeconds(4));
    internal static readonly (double Attractiveness, double Risk, TimeSpan Travel) TaskbarSpot = (0.6, 0.05, TimeSpan.FromSeconds(5));
    internal static readonly (double Attractiveness, double Risk, TimeSpan Travel) ScreenTopSpot = (0.7, 0.3, TimeSpan.FromSeconds(8));
    /// <summary>Оценка перелёта на соседний монитор и на монитор через зазор («улетел за край и прилетел», ТЗ §10.1).</summary>
    public static readonly TimeSpan AdjacentFlight = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan GapFlight = TimeSpan.FromSeconds(30);
    internal const double TerritoryAttractiveness = 0.6;
    internal const double TerritoryRisk = 0.1;

    private readonly IDesktopEnvironment _environment;
    private readonly IReadOnlyList<CapabilityCheck> _probed;
    private IReadOnlyList<Affordance> _affordances = [];
    private (AffordanceId Target, MonitorKey Monitor)? _relocation;

    /// <param name="probed">Проба при запуске (ICapabilityProbe): без C1–C4 оверлея уровень L2 недоступен.</param>
    public DesktopWorld(IDesktopEnvironment environment, IReadOnlyList<CapabilityCheck> probed)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(probed);

        _environment = environment;
        _probed = probed;
        Level = ComputeLevel(DesktopTopology.Empty);
    }

    public DesktopTopology Topology { get; private set; } = DesktopTopology.Empty;

    public IReadOnlyList<Surface> Surfaces { get; private set; } = [];

    /// <summary>Уровень, который этот мир даёт сейчас: не выше L2, ниже — если возможности пропали.</summary>
    public DesktopLevel Level { get; private set; }

    /// <summary>Монитор голубя; <c>null</c> — мониторов нет.</summary>
    public MonitorKey? PigeonMonitor { get; private set; }

    public IReadOnlyList<Affordance> Current() => _affordances;

    /// <summary>Перечитывает экран и возвращает, что изменилось с прошлого раза.</summary>
    public IReadOnlyList<DesktopWorldEvent> Refresh()
    {
        var before = Topology;
        var after = _environment.ReadTopology();
        var events = new List<DesktopWorldEvent>();

        foreach (var monitor in after.Monitors.Where(m => before.Find(m.Key) is null))
        {
            events.Add(new MonitorConnected(monitor));
        }

        foreach (var monitor in before.Monitors)
        {
            if (after.Find(monitor.Key) is not { } now)
            {
                events.Add(new MonitorDisconnected(monitor));
            }
            else if (now != monitor)
            {
                events.Add(new MonitorChanged(monitor, now));
            }
        }

        var hadMonitors = before.Monitors.Count > 0;
        var hasMonitors = after.Monitors.Count > 0;
        if (hadMonitors != hasMonitors && before != DesktopTopology.Empty)
        {
            events.Add(new CapabilityChanged(Capability.MonitorEnumeration, hasMonitors));
        }

        if (before.TaskbarsKnown != after.TaskbarsKnown && hasMonitors && hadMonitors)
        {
            events.Add(new CapabilityChanged(Capability.TaskbarGeometry, after.TaskbarsKnown));
        }

        Topology = after;
        Surfaces = ScreenSurfaces.Compute(after);

        var level = ComputeLevel(after);
        if (level != Level)
        {
            events.Add(new LevelChanged(Level, level));
            Level = level;
        }

        PlacePigeon(before, events);
        _affordances = BuildAffordances();
        return events;
    }

    /// <summary>
    /// Сообщает миру текущее намерение голубя. Пока локомоции нет (M1.5), перелёт на другой
    /// монитор считается совершённым, когда намерение Relocate к этой территории закончилось.
    /// </summary>
    public PigeonRelocated? TrackIntent(IntentKind intent, AffordanceId? target)
    {
        if (intent == IntentKind.Relocate && target is { } id)
        {
            if (_relocation?.Target != id && MonitorOfTerritory(id) is { } destination)
            {
                _relocation = (id, destination);
            }

            return null;
        }

        if (_relocation is not { } done)
        {
            return null;
        }

        _relocation = null;
        if (PigeonMonitor is not { } from || from == done.Monitor || Topology.Find(done.Monitor) is null)
        {
            return null;
        }

        PigeonMonitor = done.Monitor;
        _affordances = BuildAffordances();
        return new PigeonRelocated(from, done.Monitor);
    }

    /// <summary>Идентификатор affordance из ключа места: стабилен, пока жив объект, и между запусками.</summary>
    public static AffordanceId IdOf(PlaceKey place) => new(unchecked((long)StableHasher.HashString(place.Value)));

    private void PlacePigeon(DesktopTopology before, List<DesktopWorldEvent> events)
    {
        if (PigeonMonitor is not { } current)
        {
            PigeonMonitor = Topology.Primary?.Key;
            return;
        }

        if (Topology.Find(current) is not null)
        {
            return;
        }

        // Монитор голубя пропал: голубь «падает» на ближайший оставшийся (ТЗ §12).
        var reason = Topology.Monitors.Count == 0 ? DisplacementReason.CapabilityLost : DisplacementReason.TerritoryGone;
        var lost = before.Find(current);
        var nearest = lost is null
            ? Topology.Primary
            : Topology.Monitors.OrderBy(m => Distance(m.Bounds, lost.Bounds)).ThenByDescending(m => m.IsPrimary).FirstOrDefault();

        PigeonMonitor = nearest?.Key;
        _relocation = null;
        events.Add(new PigeonDisplaced(current, PigeonMonitor, reason));
    }

    private List<Affordance> BuildAffordances()
    {
        if (PigeonMonitor is not { } here || Topology.Find(here) is not { } home)
        {
            return [];
        }

        var affordances = new List<Affordance>();
        foreach (var surface in Surfaces.Where(s => s.Monitor == here))
        {
            var (kind, spot) = surface.Kind switch
            {
                SurfaceKind.Floor => (AffordanceKind.RestSpot, FloorSpot),
                SurfaceKind.Taskbar => (AffordanceKind.PerchSpot, TaskbarSpot),
                _ => (AffordanceKind.PerchSpot, ScreenTopSpot),
            };
            affordances.Add(new Affordance(IdOf(surface.Key), surface.Key, kind, spot.Attractiveness, spot.Risk, spot.Travel));
        }

        foreach (var other in Topology.Monitors.Where(m => m.Key != here))
        {
            var place = ScreenSurfaces.MonitorPlace(other.Key);
            var travel = Touches(home.Bounds, other.Bounds) ? AdjacentFlight : GapFlight;
            affordances.Add(new Affordance(IdOf(place), place, AffordanceKind.TerritoryZone, TerritoryAttractiveness, TerritoryRisk, travel));
        }

        return [.. affordances.OrderBy(a => a.Id.Value)];
    }

    private MonitorKey? MonitorOfTerritory(AffordanceId id)
    {
        foreach (var monitor in Topology.Monitors)
        {
            if (IdOf(ScreenSurfaces.MonitorPlace(monitor.Key)) == id)
            {
                return monitor.Key;
            }
        }

        return null;
    }

    /// <summary>L2 нужны C1–C5; C5 и C13 берутся из того, что экран отдаёт сейчас, остальное — из пробы.</summary>
    private DesktopLevel ComputeLevel(DesktopTopology topology)
    {
        var checks = _probed
            .Where(c => c.Capability is not (Capability.MonitorEnumeration or Capability.TaskbarGeometry))
            .Append(new CapabilityCheck(
                Capability.MonitorEnumeration,
                topology.Monitors.Count > 0 ? CapabilityStatus.Available : CapabilityStatus.Unavailable,
                "DesktopWorld.Refresh"))
            .ToList();

        var level = DesktopLevels.Compute(checks);
        return level > DesktopLevel.ScreenWorld ? DesktopLevel.ScreenWorld : level;
    }

    private static bool Touches(PixelRect a, PixelRect b) =>
        a.X <= b.X + b.Width && b.X <= a.X + a.Width && a.Y <= b.Y + b.Height && b.Y <= a.Y + a.Height;

    private static long Distance(PixelRect a, PixelRect b)
    {
        long dx = (a.X + a.Width / 2) - (b.X + b.Width / 2);
        long dy = (a.Y + a.Height / 2) - (b.Y + b.Height / 2);
        return dx * dx + dy * dy;
    }
}
