using System.Globalization;

namespace CompanyPaisa.Importer.Financials;

public static class FiscalYears
{
    /// <summary>
    /// The company's year end as "MM-dd". EDGAR's record ("0630") is used when it agrees, within a week, with the year the
    /// company's annual reports actually state — 52/53-week years end on a different day each year, and EDGAR's day is the
    /// usual one. When they disagree (EDGAR says 31 December for CACI, whose year ends 30 June), the annual reports win.
    /// </summary>
    public static string? YearEnd(string? edgar, DateOnly? reported)
    {
        DateOnly? recorded = edgar is { Length: 4 } && int.TryParse(edgar[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var m) &&
                             int.TryParse(edgar[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var d) && m is >= 1 and <= 12 && d >= 1 &&
                             d <= DateTime.DaysInMonth(2024, m)
            ? new DateOnly(reported?.Year ?? 2024, m, Math.Min(d, DateTime.DaysInMonth(reported?.Year ?? 2024, m)))
            : null;
        if (reported is not { } r) return recorded?.ToString("MM-dd", CultureInfo.InvariantCulture);
        // A 52/53-week year that ran into the first days of January (Onto Innovation's to 3 January 2026) is a calendar year.
        if (r.Month == 1 && r.Day <= 7) r = new DateOnly(r.Year - 1, 12, 31);
        if (recorded is { } e && Math.Min(Math.Abs(e.DayNumber - r.DayNumber), 366 - Math.Abs(e.DayNumber - r.DayNumber)) <= 7)
            return e.ToString("MM-dd", CultureInfo.InvariantCulture);
        return r.ToString("MM-dd", CultureInfo.InvariantCulture);
    }
}
