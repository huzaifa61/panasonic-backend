namespace PanasonicPrototype.Api.Services;

// Fiscal year uses a "starts-in" convention: FiscalYear = 2026 means the
// Apr-2026 -> Mar-2027 fiscal year. Fiscal month index 1 = April ... 12 = March.
public static class FiscalCalendarHelper
{
    public static (int CalendarYear, int CalendarMonth) GetCalendarMonth(int fiscalYear, int fiscalMonthIndex)
    {
        if (fiscalMonthIndex < 1 || fiscalMonthIndex > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(fiscalMonthIndex), "Fiscal month index must be between 1 and 12.");
        }

        var calendarMonth = fiscalMonthIndex + 3;
        return calendarMonth > 12 ? (fiscalYear + 1, calendarMonth - 12) : (fiscalYear, calendarMonth);
    }

    public static List<(int CalendarYear, int CalendarMonth)> GetFiscalYearMonths(int fiscalYear) =>
        Enumerable.Range(1, 12).Select(i => GetCalendarMonth(fiscalYear, i)).ToList();

    public static int GetFiscalYear(int calendarYear, int calendarMonth) =>
        calendarMonth >= 4 ? calendarYear : calendarYear - 1;

    public static int GetFiscalMonthIndex(int calendarYear, int calendarMonth) =>
        calendarMonth >= 4 ? calendarMonth - 3 : calendarMonth + 9;
}
