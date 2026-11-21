using BrPay.Boleto;

namespace BrPay.Tests.Boleto;

public class ArrecadacaoTests
{
    // Hand-computed arrecadação vector (modulo 10): product 8, segment 2
    // (saneamento), value type 6, value 00000001000 (R$ 10,00), company 0001.
    internal const string HandBarcode = "8265" + "00000001000" + "0001" + "0000000000000000000000000";
    internal const string HandLine = "826500000003" + "100000010007" + "000000000000000000000000";

    [Fact]
    public void HandComputedVector()
    {
        BoletoCode b = BoletoCode.Parse(HandBarcode);
        Assert.Equal(HandLine, b.DigitableLine);
        Assert.Equal("82650000000-3 10000001000-7 00000000000-0 00000000000-0", b.Formatted());
        Assert.Equal(BoletoType.Arrecadacao, b.Type);
        Assert.Equal(1000L, b.AmountCents);
        Assert.Null(b.Bancario);

        ArrecadacaoDetails? a = b.Arrecadacao;
        Assert.NotNull(a);
        Assert.Equal(ArrecadacaoSegment.Saneamento, a.Segment);
        Assert.False(a.ValueIsReference);
        Assert.Equal(10, a.Modulus);
        Assert.Equal("0001", a.CompanyId);
        Assert.Null(a.CnpjBase);
        Assert.Equal("Saneamento", a.Segment.GetDescription());
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void BothModuli(int valueType)
    {
        const string rest = "0123" + "4567890123456789012345678";
        string body = "83" + (char)('0' + valueType) + "00000123456" + rest;
        char dv = valueType <= 7 ? CheckDigits.Mod10(body) : CheckDigits.Mod11Arrecadacao(body);
        string code = body.Substring(0, 3) + dv + body.Substring(3);

        string line = BoletoCode.BarcodeToLine(code);
        Assert.Equal(BoletoCode.ArrecadacaoLineLength, line.Length);
        Assert.Equal(code, BoletoCode.LineToBarcode(BoletoCode.FormatLine(line)));

        BoletoCode b = BoletoCode.CreateArrecadacao(ArrecadacaoSegment.EnergiaGas, valueType, 123456, rest);
        Assert.Equal(code, b.Barcode);
        ArrecadacaoDetails? a = b.Arrecadacao;
        Assert.NotNull(a);
        Assert.Equal(valueType >= 8 ? 11 : 10, a.Modulus);
        Assert.Equal(valueType, a.ValueType);
        Assert.Equal(valueType == 7 || valueType == 9, a.ValueIsReference);
        Assert.Equal("0123", a.CompanyId);
        Assert.Equal(rest.Substring(4), a.FreeField);
    }

    [Fact]
    public void CnpjSegment()
    {
        string rest = "11222333" + new string('9', 21);
        BoletoCode b = BoletoCode.CreateArrecadacao(ArrecadacaoSegment.Cnpj, 8, 5000, rest);
        ArrecadacaoDetails? a = BoletoCode.Parse(b.Formatted()).Arrecadacao;
        Assert.NotNull(a);
        Assert.Equal("11222333", a.CnpjBase);
        Assert.Null(a.CompanyId);
        Assert.Equal(new string('9', 21), a.FreeField);
    }

    [Fact]
    public void CheckDigitErrorsNameTheBlock()
    {
        BoletoCode b = BoletoCode.CreateArrecadacao(ArrecadacaoSegment.Telecom, 8, 98765, new string('7', 29));
        for (int block = 1; block <= 4; block++)
        {
            char[] l = b.DigitableLine.ToCharArray();
            int i = ((block - 1) * 12) + 11; // the block's check digit
            l[i] = (char)('0' + ((l[i] - '0' + 1) % 10));
            var e = Assert.Throws<CheckDigitException>(() => BoletoCode.Validate(new string(l)));
            Assert.Equal(block, e.Field);
        }

        char[] code = b.Barcode.ToCharArray();
        code[3] = (char)('0' + ((code[3] - '0' + 1) % 10));
        Assert.Equal(CheckDigitException.FieldGeneral, Assert.Throws<CheckDigitException>(() => BoletoCode.Validate(new string(code))).Field);
    }

    [Theory]
    [InlineData(0, 6, 0L, "00000000000000000000000000000", BoletoError.Segment)]
    [InlineData(8, 6, 0L, "00000000000000000000000000000", BoletoError.Segment)]
    [InlineData(9, 5, 0L, "00000000000000000000000000000", BoletoError.ValueType)]
    [InlineData(9, 6, -1L, "00000000000000000000000000000", BoletoError.Amount)]
    [InlineData(9, 6, 100_000_000_000L, "00000000000000000000000000000", BoletoError.Amount)]
    [InlineData(9, 6, 0L, "1", BoletoError.FreeField)]
    [InlineData(9, 6, 0L, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaa", BoletoError.FreeField)]
    public void CreateErrors(int segment, int valueType, long value, string rest, BoletoError expected)
    {
        var e = Assert.Throws<BoletoException>(() => BoletoCode.CreateArrecadacao((ArrecadacaoSegment)segment, valueType, value, rest));
        Assert.Equal(expected, e.Error);
    }

    [Fact]
    public void SegmentNames()
    {
        for (int s = 0; s <= 10; s++)
        {
            bool defined = (s >= 1 && s <= 7) || s == 9;
            Assert.Equal(defined, ((ArrecadacaoSegment)s).GetDescription() is not null);
        }
    }

    [Fact]
    public void LineWithInvalidValueType() =>
        Assert.Equal(BoletoError.ValueType, Assert.Throws<BoletoException>(() => BoletoCode.Validate("805" + new string('0', 45))).Error);

    // Parity vectors: the outputs of the Go examples for BarcodeToLine, LineToBarcode and NewArrecadacao.
    [Fact]
    public void MatchesGoExamples()
    {
        Assert.Equal("826500000003100000010007000000000000000000000000", BoletoCode.BarcodeToLine("82650000000100000010000000000000000000000000"));
        Assert.Equal("82650000000100000010000000000000000000000000", BoletoCode.LineToBarcode("82650000000-3 10000001000-7 00000000000-0 00000000000-0"));

        BoletoCode b = BoletoCode.CreateArrecadacao(ArrecadacaoSegment.EnergiaGas, 8, 25075, "0123" + new string('0', 25));
        Assert.Equal("83810000002-7 50750123000-8 00000000000-0 00000000000-0", b.Formatted());
        Assert.Equal("Energia elétrica e gás", b.Arrecadacao?.Segment.GetDescription());
        Assert.Equal(11, b.Arrecadacao?.Modulus);
    }
}
