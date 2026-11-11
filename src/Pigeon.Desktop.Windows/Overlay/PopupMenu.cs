using System.Runtime.Versioning;
using static Pigeon.Desktop.Windows.Native;

namespace Pigeon.Desktop.Windows.Overlay;

/// <summary>Пункт меню; <c>null</c> в списке — разделитель.</summary>
/// <param name="Id">Что вернёт <see cref="PopupMenu.Show"/>, если выбран этот пункт; больше нуля.</param>
public sealed record MenuItem(int Id, string Text, bool Enabled = true);

/// <summary>
/// Контекстное меню голубя (ТЗ §11.8): системное меню Windows у курсора.
/// </summary>
/// <remarks>
/// Меню закрывается по клику мимо, только если его окно — активное, поэтому на время меню окно
/// голубя становится активным. После закрытия активность возвращается окну, которое было
/// активным до меню: голубь фокус не забирает.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class PopupMenu
{
    private const uint MfString = 0x0000;
    private const uint MfGrayed = 0x0001;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNoNotify = 0x0080;
    private const uint WmNull = 0x0000;

    /// <summary>Показывает меню в точке экрана и ждёт выбора; <c>null</c> — меню закрыто без выбора.</summary>
    public static int? Show(IntPtr owner, int x, int y, IReadOnlyList<MenuItem?> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return null;
        }

        var previous = GetForegroundWindow();
        try
        {
            foreach (var item in items)
            {
                if (item is null)
                {
                    AppendMenuW(menu, MfSeparator, UIntPtr.Zero, null);
                }
                else
                {
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(item.Id, nameof(items));
                    AppendMenuW(menu, MfString | (item.Enabled ? 0 : MfGrayed), (UIntPtr)item.Id, item.Text);
                }
            }

            SetForegroundWindow(owner);
            var chosen = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd | TpmNoNotify, x, y, owner, IntPtr.Zero);

            // Иначе повторное меню закрывается сразу (известная особенность TrackPopupMenu).
            PostMessageW(owner, WmNull, IntPtr.Zero, IntPtr.Zero);
            return chosen > 0 ? chosen : null;
        }
        finally
        {
            DestroyMenu(menu);
            if (previous != IntPtr.Zero && previous != owner)
            {
                SetForegroundWindow(previous);
            }
        }
    }
}

/// <summary>Глобальная позиция курсора (C7, ТЗ §8.2): <c>GetCursorPos</c>, опрос — без хуков мыши.</summary>
[SupportedOSPlatform("windows")]
public static class CursorPosition
{
    /// <summary>Курсор в физических пикселях виртуального экрана; <c>null</c> — система его не отдала (защищённый рабочий стол).</summary>
    public static (int X, int Y)? Read() => GetCursorPos(out var point) ? (point.X, point.Y) : null;
}
