using BrPay.Boleto;

namespace BrPay.Tests.Boleto;

public class BancarioTests
{
    // Hand-computed bancário vector: bank 001, currency 9, factor 1000,
    // amount 0000010000 (R$ 100,00), free field of 24 zeros followed by 7.
    // DAC: 7*2 + 1*7 + 1*8 + 9*9 + 1*2 = 112, 112 % 11 = 2, 11 - 2 = 9.
    internal const string HandBarcode = "0019" + "9" + "1000" + "0000010000" + "000000000000000000000000" + "7";
    internal const string HandLine = "0019000009" + "00000000000" + "00000000075" + "9" + "10000000010000";
    internal const string HandFormatted = "00190.00009 00000.000000 00000.000075 9 10000000010000";

    [Fact]
    public void HandComputedVector()
    {
        BoletoCode b = BoletoCode.Parse(HandBarcode);
        Assert.Equal(HandLine, b.DigitableLine);
        Assert.Equal(HandFormatted, b.Formatted());
        Assert.Equal(HandFormatted, b.ToString());
        Assert.Equal(BoletoType.Bancario, b.Type);
        Assert.Equal(10000L, b.AmountCents);
        Assert.Equal(100m, b.Amount);
        Assert.Null(b.Arrecadacao);

        BancarioDetails? bk = b.Bancario;
        Assert.NotNull(bk);
        Assert.Equal("001", bk.BankCode);
        Assert.Equal("Banco do Brasil", bk.BankName);
        Assert.Equal(9, bk.Currency);
        Assert.Equal(1000, bk.Factor);

        BoletoCode built = BoletoCode.CreateBancario("001", new DateOnly(2000, 7, 3), 10000, new string('0', 24) + "7");
        Assert.Equal(HandBarcode, built.Barcode);
        Assert.Equal(1000, built.Bancario?.Factor);
    }

    // Parity vectors: the outputs of the Go examples for boleto.Parse and boleto.NewBancario.
    [Fact]
    public void MatchesGoExamples()
    {
        BoletoCode b = BoletoCode.Parse("00190.00009 00000.000000 00000.000075 9 10000000010000");
        Assert.Equal("bancário Banco do Brasil", b.Type.GetDisplayName() + " " + b.Bancario?.BankName);
        Assert.Equal("00199100000000100000000000000000000000000007", b.Barcode);
        Assert.Equal(new DateOnly(2025, 2, 22), b.Bancario?.DueDateAt(new DateOnly(2025, 3, 1)));

        BoletoCode built = BoletoCode.CreateBancario("341", new DateOnly(2026, 12, 10), 15990, "1090100000012345678900001");
        Assert.Equal("34191.09016 00000.012344 56789.000017 9 16560000015990", built.Formatted());
        Assert.Equal(1656, built.Bancario?.Factor);
    }

    public static readonly TheoryData<string, string, long, string> RoundTripCases = new()
    {
        { "341", "2026-12-10", 123456L, "1090100000012345678900001" },
        { "237", string.Empty, 0L, "9999999999999999999999999" },
        { "104", "2025-02-21", 9_999_999_999L, "0123456789012345678901234" },
        { "999", "2049-10-13", 1L, "5555500000111112222233333" },
    };

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void RoundTrip(string bank, string dueText, long amount, string free)
    {
        DateOnly? due = dueText.Length == 0 ? null : DateOnly.Parse(dueText, System.Globalization.CultureInfo.InvariantCulture);
        BoletoCode b = BoletoCode.CreateBancario(bank, due, amount, free);
        Assert.Equal(b.DigitableLine, BoletoCode.BarcodeToLine(b.Barcode));
        Assert.Equal(b.Barcode, BoletoCode.LineToBarcode(b.Formatted()));
        foreach (string input in new[] { b.Barcode, b.DigitableLine, b.Formatted() })
        {
            BoletoCode p = BoletoCode.Parse(input);
            Assert.Equal(b.Barcode, p.Barcode);
            Assert.Equal(amount, p.AmountCents);
            Assert.Equal(free, p.Bancario?.FreeField);
            Assert.Equal(bank, p.Bancario?.BankCode);
            if (due is null)
            {
                Assert.Equal(0, p.Bancario?.Factor);
                Assert.Null(p.Bancario?.DueDateAt(new DateOnly(2026, 1, 1)));
            }
            else
            {
                Assert.Equal(due, p.Bancario?.DueDateAt(due.Value));
            }
        }
    }

    [Fact]
    public void UnknownBankHasNoName()
    {
        BoletoCode b = BoletoCode.CreateBancario("999", null, 1, new string('1', 25));
        Assert.Null(b.Bancario?.BankName);
        Assert.Null(b.Bancario?.DueDate());
    }

    [Theory]
    [InlineData("01", 0L, "0000000000000000000000000", BoletoError.BankCode)]
    [InlineData("0a1", 0L, "0000000000000000000000000", BoletoError.BankCode)]
    [InlineData("800", 0L, "0000000000000000000000000", BoletoError.ProductId)]
    [InlineData("001", -1L, "0000000000000000000000000", BoletoError.Amount)]
    [InlineData("001", 10_000_000_000L, "0000000000000000000000000", BoletoError.Amount)]
    [InlineData("001", 0L, "123", BoletoError.FreeField)]
    [InlineData("001", 0L, "xxxxxxxxxxxxxxxxxxxxxxxxx", BoletoError.FreeField)]
    public void CreateErrors(string bank, long amount, string free, BoletoError expected)
    {
        var e = Assert.Throws<BoletoException>(() => BoletoCode.CreateBancario(bank, null, amount, free));
        Assert.Equal(expected, e.Error);
    }

    [Fact]
    public void CreateRejectsDueDateBeforeFirstFactor()
    {
        var e = Assert.Throws<BoletoException>(() => BoletoCode.CreateBancario("001", new DateOnly(1999, 1, 1), 0, new string('0', 25)));
        Assert.Equal(BoletoError.DueDate, e.Error);
        Assert.Equal("boleto: due date out of range: 1999-01-01 is before 2000-07-03", e.Message);
    }

    private static string Flip(string s, int i)
    {
        char[] b = s.ToCharArray();
        b[i] = (char)('0' + ((b[i] - '0' + 1) % 10));
        return new string(b);
    }

    [Theory]
    [InlineData(HandLine, 2, 1)]
    [InlineData(HandLine, 9, 1)]
    [InlineData(HandLine, 15, 2)]
    [InlineData(HandLine, 25, 3)]
    [InlineData(HandLine, 32, CheckDigitException.FieldGeneral)]
    [InlineData(HandLine, 40, CheckDigitException.FieldGeneral)]
    [InlineData(HandBarcode, 4, CheckDigitException.FieldGeneral)]
    [InlineData(HandBarcode, 30, CheckDigitException.FieldGeneral)]
    public void CheckDigitErrorsNameTheField(string valid, int position, int field)
    {
        var e = Assert.Throws<CheckDigitException>(() => BoletoCode.Validate(Flip(valid, position)));
        Assert.Equal(field, e.Field);
        Assert.Equal(BoletoError.CheckDigit, e.Error);
        Assert.NotEqual(e.Expected, e.Got);
    }

    [Fact]
    public void CheckDigitMessages()
    {
        Assert.Equal("boleto: check digit mismatch in field 2: expected 3, got 4", new CheckDigitException(2, '3', '4').Message);
        Assert.Equal("boleto: general check digit mismatch: expected 3, got 4", new CheckDigitException(CheckDigitException.FieldGeneral, '3', '4').Message);

        // Parity vector: the output of the Go ExampleValidate.
        var e = Assert.Throws<CheckDigitException>(() => BoletoCode.Validate("00190.00009 00000.000001 00000.000075 9 10000000010000"));
        Assert.Equal("check field 2: expected 0, got 1", "check field " + e.Field + ": expected " + e.Expected + ", got " + e.Got);
    }

    [Theory]
    [InlineData("", BoletoError.Length)]
    [InlineData("123", BoletoError.Length)]
    [InlineData("1111111111111111111111111111111111111111111111111", BoletoError.Length)]
    [InlineData("1111111111111111111111111111111111111111111111", BoletoError.Length)]
    [InlineData("0019000009x0000000000000000000075910000000010000", BoletoError.Characters)]
    [InlineData("00190000090000000000000000000075910000000010000é", BoletoError.Characters)]
    [InlineData("80190000090000000000000000000075910000000010000", BoletoError.ProductId)]
    [InlineData("81000000000000000000000000000000000000000000", BoletoError.ValueType)]
    [InlineData("810000000000000000000000000000000000000000000000", BoletoError.ValueType)]
    public void ParseErrors(string input, BoletoError expected)
    {
        var e = Assert.Throws<BoletoException>(() => BoletoCode.Parse(input));
        Assert.Equal(expected, e.Error);
        Assert.False(BoletoCode.IsValid(input));
        Assert.False(BoletoCode.TryParse(input, out _, out BoletoException? error));
        Assert.NotNull(error);
        Assert.Equal(expected, error.Error);
    }

    [Fact]
    public void ConversionErrors()
    {
        Assert.Equal(BoletoError.Length, Assert.Throws<BoletoException>(() => BoletoCode.BarcodeToLine(HandLine)).Error);
        Assert.Equal(BoletoError.Characters, Assert.Throws<BoletoException>(() => BoletoCode.BarcodeToLine("x")).Error);
        Assert.Throws<CheckDigitException>(() => BoletoCode.BarcodeToLine(HandBarcode.Substring(0, 4) + "0" + HandBarcode.Substring(5)));
        Assert.Equal(BoletoError.Length, Assert.Throws<BoletoException>(() => BoletoCode.LineToBarcode(HandBarcode)).Error);
        Assert.Equal(BoletoError.Characters, Assert.Throws<BoletoException>(() => BoletoCode.LineToBarcode("x")).Error);
        Assert.Throws<CheckDigitException>(() => BoletoCode.LineToBarcode(HandLine.Substring(0, 9) + "0" + HandLine.Substring(10)));
        Assert.Equal(BoletoError.ProductId, Assert.Throws<BoletoException>(() => BoletoCode.LineToBarcode(new string('0', 48))).Error);
    }

    [Fact]
    public void DisplayNames()
    {
        Assert.Equal("bancário", BoletoType.Bancario.GetDisplayName());
        Assert.Equal("arrecadação", BoletoType.Arrecadacao.GetDisplayName());
        Assert.Equal("unknown", BoletoType.Unknown.GetDisplayName());
        Assert.Equal("123", BoletoCode.FormatLine("123"));
    }
}
