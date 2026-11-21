using System.Globalization;

namespace BrPay.Boleto;

/// <summary>
/// The bancário due-date factor: the number of days since 1997-10-07,
/// written with four digits, including the February 2025 rollover.
/// </summary>
/// <remarks>
/// Factor 9999 was reached on 2025-02-21 and, as defined by FEBRABAN, the
/// factor restarted at 1000 on 2025-02-22, so the same factor now maps to
/// dates 9000 days apart. <see cref="ToDate"/> resolves the ambiguity against a reference date.
/// </remarks>
public static class DueDateFactor
{
    /// <summary>The first factor of a cycle (2000-07-03, then 2025-02-22).</summary>
    public const int Min = 1000;

    /// <summary>The last factor of a cycle (2025-02-21, then 2049-10-13).</summary>
    public const int Max = 9999;

    /// <summary>The number of days in one factor cycle (1000–9999).</summary>
    public const int Cycle = Max - Min + 1;

    /// <summary>Days before the reference date where <see cref="ToDate"/> looks for the due date (FEBRABAN guidance).</summary>
    public const int WindowPast = 3000;

    /// <summary>Days after the reference date where <see cref="ToDate"/> looks for the due date (FEBRABAN guidance).</summary>
    public const int WindowFuture = 5500;

    /// <summary>The base date of the factor, 1997-10-07 (factor 0 of the first cycle).</summary>
    public static DateOnly BaseDate => new(1997, 10, 7);

    /// <summary>
    /// Returns the factor (1000–9999) that encodes a date. Dates from
    /// 2025-02-22 onwards use the restarted cycle.
    /// </summary>
    /// <param name="date">The due date.</param>
    /// <returns>The factor.</returns>
    /// <exception cref="BoletoException">The date is before 2000-07-03 (<see cref="BoletoError.DueDate"/>).</exception>
    public static int FromDate(DateOnly date) =>
        TryFromDate(date, out int factor)
            ? factor
            : throw BoletoException.Of(BoletoError.DueDate, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " is before 2000-07-03");

    /// <summary>Returns the factor that encodes a date without throwing.</summary>
    /// <param name="date">The due date.</param>
    /// <param name="factor">The factor, when the date can be encoded.</param>
    /// <returns><see langword="false"/> for dates before 2000-07-03.</returns>
    public static bool TryFromDate(DateOnly date, out int factor)
    {
        int d = date.DayNumber - BaseDate.DayNumber;
        if (d < Min)
        {
            factor = 0;
            return false;
        }

        factor = ((d - Min) % Cycle) + Min;
        return true;
    }

    /// <summary>
    /// Resolves a factor to a calendar date using a reference date to pick
    /// the cycle. Returns <see langword="null"/> for factor 0 ("sem
    /// vencimento") and for values outside 0–9999.
    /// </summary>
    /// <remarks>
    /// A factor f in 1000–9999 denotes 1997-10-07 + f + 9000·k days for some
    /// cycle k ≥ 0. The candidate inside the FEBRABAN window
    /// [reference − 3000 days, reference + 5500 days] is chosen; when none
    /// falls inside (a 500-day gap exists between consecutive windows) the
    /// candidate closest to the reference wins, preferring the earlier one on
    /// ties. Legacy factors 1–999 predate any rollover and always map to the
    /// first cycle. The rule is identical to the Go implementation.
    /// </remarks>
    /// <param name="factor">The factor.</param>
    /// <param name="reference">The reference date, usually today.</param>
    /// <returns>The due date, or <see langword="null"/>.</returns>
    public static DateOnly? ToDate(int factor, DateOnly reference)
    {
        if (factor <= 0 || factor > Max)
        {
            return null;
        }

        int baseDay = BaseDate.DayNumber;
        if (factor < Min)
        {
            return DateOnly.FromDayNumber(baseDay + factor);
        }

        long r = reference.DayNumber - baseDay;
        long x = factor;
        long lo = r - WindowPast - x;
        if (lo > 0)
        {
            x += (lo + Cycle - 1) / Cycle * Cycle;
        }

        // x is now the first candidate not before the window start.
        if (x > r + WindowFuture && x - Cycle >= factor)
        {
            long prev = x - Cycle;
            if (r - prev <= x - r)
            {
                x = prev;
            }
        }

        long day = baseDay + x;
        if (day > DateOnly.MaxValue.DayNumber)
        {
            return null;
        }

        return DateOnly.FromDayNumber((int)day);
    }
}
