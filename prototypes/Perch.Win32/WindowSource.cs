using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pigeon.World.Desktop.Perches;
using static Pigeon.Prototypes.Perch.Native;

namespace Pigeon.Prototypes.Perch;

internal readonly record struct DesktopWindow(IntPtr Handle, PixelBox Box);

/// <summary>
/// Снимок видимых окон верхнего уровня в z-order (верхнее первым). Границы — видимая рамка DWM,
/// без невидимых полей под тень. Заголовки не читаются; имя класса нужно только чтобы отсеять
/// рабочий стол и панель задач.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowSource
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    /// <param name="precise">Окно, для которого нужна точная видимая рамка DWM; для остальных
    /// берётся GetWindowRect: он дешевле и чуть шире из-за невидимых полей, то есть перекрытие
    /// оценивается с запасом.</param>
    public static List<DesktopWindow> Snapshot(IntPtr exclude, IntPtr precise)
    {
        var list = new List<DesktopWindow>(64);
        var name = new char[64];
        EnumWindows((window, _) =>
        {
            if (window == exclude || !IsWindowVisible(window) || IsIconic(window))
            {
                return true;
            }

            if ((GetWindowLongPtrW(window, GwlExStyle).ToInt64() & WsExToolWindow) != 0)
            {
                return true;
            }

            var length = GetClassNameW(window, name, name.Length);
            if (ShellClasses.Contains(new string(name, 0, length)))
            {
                return true;
            }

            if (DwmGetWindowAttributeInt(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            PixelBox? box = window == precise ? Bounds(window)
                : GetWindowRect(window, out var r) ? new PixelBox(r.Left, r.Top, r.Right, r.Bottom) : null;
            if (box is { Width: > 0, Height: > 0 } b)
            {
                list.Add(new DesktopWindow(window, b));
            }

            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static PixelBox? Bounds(IntPtr window) =>
        DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out var r, Marshal.SizeOf<Rect>()) == 0
            ? new PixelBox(r.Left, r.Top, r.Right, r.Bottom)
            : null;
}
