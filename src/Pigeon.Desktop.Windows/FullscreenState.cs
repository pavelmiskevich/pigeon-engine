using System.Runtime.Versioning;
using static Pigeon.Desktop.Windows.Native;

namespace Pigeon.Desktop.Windows;

/// <summary>
/// Идёт ли полноэкранное приложение или презентация (C14, ТЗ §11.7):
/// <c>SHQueryUserNotificationState</c>. Тогда голубь затихает на всех мониторах.
/// </summary>
public static class FullscreenState
{
    private const int Busy = 2;
    private const int Direct3DFullScreen = 3;
    private const int PresentationMode = 4;
    private const int StoreApp = 7;

    /// <summary>
    /// Значит ли состояние <c>QUERY_USER_NOTIFICATION_STATE</c> тишину: полноэкранное приложение,
    /// игра через Direct3D, режим презентации или приложение Store во весь экран. Заблокированный
    /// экран и «тихие часы» Windows — нет: голубя и так не видно или тишину решает человек.
    /// </summary>
    public static bool IsQuiet(int state) => state is Busy or Direct3DFullScreen or PresentationMode or StoreApp;

    /// <summary>Спросить Windows сейчас; ошибка вызова — не тихо.</summary>
    [SupportedOSPlatform("windows")]
    public static bool Read() => SHQueryUserNotificationState(out var state) == 0 && IsQuiet(state);
}
