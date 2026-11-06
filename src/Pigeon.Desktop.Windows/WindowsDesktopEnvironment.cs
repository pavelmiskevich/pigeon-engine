using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pigeon.Common.Hashing;
using Pigeon.World.Desktop.Capabilities;
using Pigeon.World.Desktop.Screen;
using static Pigeon.Desktop.Windows.Native;

namespace Pigeon.Desktop.Windows;

/// <summary>
/// Топология экрана Windows для Desktop World L2 (ТЗ §11.3, §11.5): мониторы через
/// EnumDisplayMonitors и GetDpiForMonitor в режиме Per-Monitor V2 — координаты в физических
/// пикселях, — панели задач через SHAppBarMessage и окна Shell_SecondaryTrayWnd.
/// </summary>
/// <remarks>
/// Ключ монитора — хеш идентификатора устройства (EnumDisplayDevices с
/// EDD_GET_DEVICE_INTERFACE_NAME): он не меняется между запусками и при смене порядка мониторов,
/// а сам идентификатор с моделью монитора наружу не выходит. Если идентификатора нет, берётся имя
/// вывода (<c>\\.\DISPLAY1</c>) — оно стабильно, пока не меняют подключение.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopEnvironment : IDesktopEnvironment
{
    private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";
    private static readonly IntPtr PerMonitorAwareV2 = -4;

    public DesktopTopology ReadTopology()
    {
        var previousAwareness = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        try
        {
            var monitors = ReadMonitors();
            var (taskbars, known) = ReadTaskbars(monitors);
            return new DesktopTopology(monitors, taskbars, known);
        }
        finally
        {
            if (previousAwareness != IntPtr.Zero)
            {
                SetThreadDpiAwarenessContext(previousAwareness);
            }
        }
    }

    private static List<MonitorState> ReadMonitors()
    {
        var monitors = new List<MonitorState>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfoEx(monitor, ref info))
            {
                var scale = GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
                monitors.Add(new MonitorState(
                    KeyOf(info.Device), ToRect(info.Monitor), ToRect(info.Work), scale, (info.Flags & MonitorInfoFPrimary) != 0));
            }

            return true;
        }, IntPtr.Zero);

        // Стабильный порядок: слева направо, сверху вниз — не зависит от порядка перечисления.
        return [.. monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)];
    }

    private static MonitorKey KeyOf(string outputName)
    {
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        var id = EnumDisplayDevices(outputName, 0, ref device, EddGetDeviceInterfaceName) && !string.IsNullOrEmpty(device.DeviceId)
            ? device.DeviceId
            : outputName;
        return new MonitorKey("m" + StableHasher.HashString(id.ToUpperInvariant()).ToString("x16", CultureInfo.InvariantCulture)[..8]);
    }

    private static (List<TaskbarState> Taskbars, bool Known) ReadTaskbars(List<MonitorState> monitors)
    {
        var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        if (SHAppBarMessage(AbmGetTaskbarPos, ref data) == UIntPtr.Zero)
        {
            return ([], false);
        }

        var state = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        var autoHide = ((uint)SHAppBarMessage(AbmGetState, ref state) & AbsAutoHide) != 0;
        var taskbars = new List<TaskbarState>();

        // Скрывающаяся панель большую часть времени убрана за край: стоять на ней нельзя.
        if (!autoHide)
        {
            AddTaskbar(taskbars, monitors, ToRect(data.Rect));
            var className = new char[64];
            EnumWindows((window, _) =>
            {
                var length = GetClassName(window, className, className.Length);
                if (new string(className, 0, Math.Max(0, length)) == SecondaryTaskbarClass
                    && IsWindowVisible(window)
                    && GetWindowRect(window, out var rect))
                {
                    AddTaskbar(taskbars, monitors, ToRect(rect));
                }

                return true;
            }, IntPtr.Zero);
        }

        return (taskbars, true);
    }

    /// <summary>Панель относится к монитору, в котором её центр, и прижата к его ближайшему краю.</summary>
    private static void AddTaskbar(List<TaskbarState> taskbars, List<MonitorState> monitors, PixelRect bar)
    {
        var centerX = bar.X + bar.Width / 2;
        var centerY = bar.Y + bar.Height / 2;
        var monitor = monitors.FirstOrDefault(m =>
            centerX >= m.Bounds.X && centerX < m.Bounds.X + m.Bounds.Width
            && centerY >= m.Bounds.Y && centerY < m.Bounds.Y + m.Bounds.Height);
        if (monitor is null || taskbars.Any(t => t.Monitor == monitor.Key))
        {
            return;
        }

        var b = monitor.Bounds;
        var edge = bar.Width >= bar.Height
            ? (centerY - b.Y < b.Y + b.Height - centerY ? ScreenEdge.Top : ScreenEdge.Bottom)
            : (centerX - b.X < b.X + b.Width - centerX ? ScreenEdge.Left : ScreenEdge.Right);
        taskbars.Add(new TaskbarState(monitor.Key, bar, edge));
    }

    private static PixelRect ToRect(Rect rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}
