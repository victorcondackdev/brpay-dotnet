using BrPay.Boleto;

namespace BrPay.Tests.Boleto;

public class DueDateFactorTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void BaseArithmetic()
    {
        DateOnly b = DueDateFactor.BaseDate;
        Assert.Equal(D(2000, 7, 3), b.AddDays(1000));
        Assert.Equal(D(2025, 2, 21), b.AddDays(9999));
        Assert.Equal(D(2022, 5, 29), b.AddDays(DueDateFactor.Cycle));
        Assert.Equal(D(2025, 2, 22), b.AddDays(DueDateFactor.Cycle + 1000));
    }

    public static readonly TheoryData<string, int, DateOnly, DateOnly> Cases = new()
    {
        { "last day of cycle 1", 9999, D(2025, 2, 21), D(2025, 2, 21) },
        { "first day of cycle 2", 1000, D(2025, 2, 22), D(2025, 2, 22) },
        { "9999 seen after rollover", 9999, D(2025, 3, 15), D(2025, 2, 21) },
        { "1000 seen just before rollover", 1000, D(2025, 2, 10), D(2025, 2, 22) },
        { "1000 seen in 2005 is 2000", 1000, D(2005, 1, 1), D(2000, 7, 3) },
        { "1000 in the window gap picks nearest (2000)", 1000, D(2010, 1, 1), D(2000, 7, 3) },
        { "1000 in the window gap picks nearest (2025)", 1000, D(2012, 1, 1), D(2025, 2, 22) },
        { "cycle 2 mid-range", 1637, D(2026, 11, 21), D(2026, 11, 21) },
        { "old reference keeps cycle 1", 5000, D(2011, 1, 1), D(1997, 10, 7).AddDays(5000) },
        { "future reference uses cycle 3", 1000, D(2049, 10, 14), D(2049, 10, 14) },
        { "reference before base", 1500, D(1990, 1, 1), D(1997, 10, 7).AddDays(1500) },
        { "legacy factor", 500, D(2026, 1, 1), D(1997, 10, 7).AddDays(500) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ToDate(string name, int factor, DateOnly reference, DateOnly expected)
    {
        Assert.NotNull(name);
        Assert.Equal(expected, DueDateFactor.ToDate(factor, reference));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10000)]
    public void NoDueDate(int factor) => Assert.Null(DueDateFactor.ToDate(factor, D(2026, 1, 1)));

    [Fact]
    public void FromDate()
    {
        Assert.Equal(1000, DueDateFactor.FromDate(D(2000, 7, 3)));
        Assert.Equal(9999, DueDateFactor.FromDate(D(2025, 2, 21)));
        Assert.Equal(1000, DueDateFactor.FromDate(D(2025, 2, 22)));
        Assert.Equal(1001, DueDateFactor.FromDate(D(2025, 2, 23)));
        Assert.Equal(9999, DueDateFactor.FromDate(D(2049, 10, 13)));
        Assert.Equal(1000, DueDateFactor.FromDate(D(2049, 10, 14)));

        foreach (DateOnly d in new[] { default(DateOnly), D(2000, 7, 2), D(1997, 10, 7) })
        {
            Assert.Equal(BoletoError.DueDate, Assert.Throws<BoletoException>(() => DueDateFactor.FromDate(d)).Error);
            Assert.False(DueDateFactor.TryFromDate(d, out _));
        }
    }

    // Parity vectors: the outputs of the Go examples for DueDateAt and FactorFor.
    [Fact]
    public void MatchesGoExamples()
    {
        Assert.Equal(D(2000, 7, 3), DueDateFactor.ToDate(1000, D(2001, 1, 1)));
        Assert.Equal(D(2025, 2, 22), DueDateFactor.ToDate(1000, D(2026, 1, 1)));
        Assert.Equal(9999, DueDateFactor.FromDate(D(2025, 2, 21)));
        Assert.Equal(1000, DueDateFactor.FromDate(D(2025, 2, 22)));
    }

    // Every date from 2000 to 2060 must survive FromDate -> ToDate when the
    // reference date is the due date itself or a few weeks around it.
    [Fact]
    public void RoundTripsEveryDayFrom2000To2060()
    {
        for (DateOnly d = D(2000, 7, 3); d < D(2060, 1, 1); d = d.AddDays(1))
        {
            int f = DueDateFactor.FromDate(d);
            foreach (int offset in new[] { 0, -30, 30 })
            {
                DateOnly reference = d.AddDays(offset);
                DateOnly? got = DueDateFactor.ToDate(f, reference);
                if (got != d)
                {
                    Assert.Fail($"round trip {d:yyyy-MM-dd} (factor {f}, reference {reference:yyyy-MM-dd}) = {got:yyyy-MM-dd}");
                }
            }
        }
    }

    [Fact]
    public void ExtremeReferencesDoNotThrow()
    {
        for (int factor = -1; factor <= 10000; factor++)
        {
            DateOnly? late = DueDateFactor.ToDate(factor, DateOnly.MaxValue);
            DateOnly? early = DueDateFactor.ToDate(factor, DateOnly.MinValue);
            Assert.Equal(factor is >= 1 and <= 9999, early.HasValue);
            Assert.True(late is null || factor < DueDateFactor.Min || late.Value > DateOnly.MaxValue.AddDays(-DueDateFactor.Cycle));
        }
    }
}
