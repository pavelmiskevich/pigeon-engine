namespace Pigeon.Desktop.Rendering;

/// <summary>Как ждать до следующего оборота цикла хоста.</summary>
/// <param name="AwaitComposition">Ждать композиции DWM — кадры идут с частотой экрана.</param>
/// <param name="Wake">Иначе — спать до этого момента (не раньше «сейчас»), если не придёт сообщение.</param>
public readonly record struct Pace(bool AwaitComposition, TimeSpan Wake);

/// <summary>
/// Темп цикла хоста (ТЗ §37): в движении кадры идут по композиции, на месте процесс спит до смены
/// кадра клипа, опроса экрана или опроса курсора. Без кадра — окно спрятано или голубь за краем —
/// ни смена кадра, ни движение цикл не будят: рисовать некуда (#48).
/// </summary>
public static class FramePacing
{
    /// <param name="hasFrame">Голубь виден, и у цикла есть что рисовать.</param>
    /// <param name="animating">Голубь движется или меняет размер — кадры нужны 60 раз в секунду.</param>
    /// <param name="nextChange">Когда сменится кадр клипа; без кадра не учитывается.</param>
    /// <param name="nextRefresh">Когда опросить мониторы и поверхности экрана.</param>
    /// <param name="pollAt">Дольше этого не спать: курсор, симуляция и конец «скрыт на час».</param>
    public static Pace Next(bool hasFrame, bool animating, TimeSpan nextChange, TimeSpan nextRefresh, TimeSpan pollAt, TimeSpan now)
    {
        if (hasFrame && animating)
        {
            return new Pace(AwaitComposition: true, now);
        }

        var wake = nextRefresh < pollAt ? nextRefresh : pollAt;
        if (hasFrame && nextChange < wake)
        {
            wake = nextChange;
        }

        return new Pace(AwaitComposition: false, wake > now ? wake : now);
    }
}
