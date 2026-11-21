using BrPay.Emv;
using BrPay.Pix;
using static BrPay.Tests.Pix.TestPayloads;

namespace BrPay.Tests.Pix;

public class BrCodeTests
{
    [Fact]
    public void DecodeStatic()
    {
        BrCode b = BrCode.Decode("  " + BcbExample + "\r\n");
        Assert.True(b.IsStatic);
        Assert.False(b.IsDynamic);
        Assert.True(b.IsReusable);
        Assert.Null(b.PointOfInitiation);
        Assert.Equal("26", b.MerchantAccountId);
        Assert.Equal("123e4567-e12b-12d1-a456-426655440000", b.Key);
        Assert.Equal(PixKeyType.Evp, b.KeyType);
        Assert.Equal("0000", b.Mcc);
        Assert.Equal("986", b.Currency);
        Assert.Null(b.RawAmount);
        Assert.Equal(0L, b.Cents);
        Assert.Equal(0m, b.Amount);
        Assert.Equal("BR", b.CountryCode);
        Assert.Equal("Fulano de Tal", b.MerchantName);
        Assert.Equal("BRASILIA", b.MerchantCity);
        Assert.Null(b.TxId);
        Assert.Equal("1D3D", b.Crc);
        Assert.Equal(BrCode.Gui, b.Templates["26"]);
        Assert.Equal(9, b.Fields.Count);
        Assert.Equal("***", b.Template("62")?.Children[0].Value);
    }

    [Fact]
    public void DecodeFull()
    {
        string body = "000201010211"
            + Obj("26", Obj("00", "BR.GOV.BCB.PIX") + Obj("01", "52998224725") + Obj("02", "Almoco!"))
            + Obj("27", Obj("00", "br.com.other") + Obj("01", "abcdefghij"))
            + "52045812" + "5303986" + "540510.50" + "5802BR" + Obj("59", "Joao Silva") + Obj("60", "Sao Paulo") + "610801311000"
            + "62140510PEDIDO1234"
            + Obj("80", Obj("00", "br.gov.bcb.future") + Obj("01", "data"));
        BrCode b = BrCode.Decode(WithCrc(body));
        Assert.Equal("52998224725", b.Key);
        Assert.Equal(PixKeyType.Cpf, b.KeyType);
        Assert.Equal("Almoco!", b.Description);
        Assert.Equal(1050L, b.Cents);
        Assert.Equal(10.50m, b.Amount);
        Assert.Equal("10.50", b.RawAmount);
        Assert.Equal("PEDIDO1234", b.TxId);
        Assert.Equal("01311000", b.PostalCode);
        Assert.Equal("5812", b.Mcc);
        Assert.Equal("11", b.PointOfInitiation);
        Assert.True(b.IsReusable);
        Assert.Equal("BR.GOV.BCB.PIX", b.Templates["26"]);
        Assert.Equal("br.com.other", b.Templates["27"]);
        Assert.Equal("br.gov.bcb.future", b.Templates["80"]);
        Assert.Equal("data", b.Template("80")?.Children[1].Value);
    }

    [Fact]
    public void DecodeDynamic()
    {
        string body = "000201010212" + "26420014br.gov.bcb.pix2520pix.example.com/qr/1"
            + "52040000" + "5303986" + "5802BR" + "5903Loj" + "6002RJ" + "62070503***";
        BrCode b = BrCode.Decode(WithCrc(body));
        Assert.True(b.IsDynamic);
        Assert.False(b.IsStatic);
        Assert.False(b.IsReusable);
        Assert.Equal("pix.example.com/qr/1", b.Url);
        Assert.Equal(PixKeyType.Unknown, b.KeyType);
        Assert.Null(b.Key);
    }

    private const string Tail = "52040000" + "5303986" + "5802BR" + "5903Loj" + "6002RJ" + "62070503***";

    private static readonly string Account = Obj("26", Obj("00", BrCode.Gui) + Obj("01", "a@b.c.de"));

    [Fact]
    public void DecodeEmvErrorsPassThrough()
    {
        Assert.Equal(EmvErrorKind.Syntax, Assert.Throws<EmvParseException>(() => BrCode.Decode("000201XX")).Kind);
        Assert.Equal(EmvErrorKind.Structure, Assert.Throws<EmvParseException>(() => BrCode.Decode(WithCrc("000202" + Account + Tail))).Kind);
        Assert.Throws<EmvCrcException>(() => BrCode.Decode(BcbExample.Substring(0, BcbExample.Length - 4) + "0000"));
    }

    [Fact]
    public void DecodePixErrors()
    {
        AssertPixError(PixError.NotPix, WithCrc("000201" + Obj("26", Obj("00", "br.com.other")) + Tail));
        AssertPixError(PixError.KeyAndUrl, WithCrc("000201" + Obj("26", Obj("00", BrCode.Gui) + Obj("01", "k") + Obj("25", "x.com/qr/1")) + Tail));
        AssertPixError(PixError.NoKeyOrUrl, WithCrc("000201" + Obj("26", Obj("00", BrCode.Gui) + Obj("02", "whatever")) + Tail));
        AssertPixError(PixError.InvalidAmount, WithCrc("000201" + Account + "540510,50" + Tail));
        AssertPixError(PixError.MissingField, WithCrc("000201" + Account + Tail.Replace("5903Loj", string.Empty, StringComparison.Ordinal)));
        AssertPixError(PixError.MissingField, WithCrc("000201" + Account + Tail.Replace("52040000", string.Empty, StringComparison.Ordinal)));

        var e = Assert.Throws<PixException>(() => BrCode.Decode(WithCrc("000201" + Account + Tail.Replace("5903Loj", string.Empty, StringComparison.Ordinal))));
        Assert.Equal("pix: missing required field: 59 (merchant name)", e.Message);

        static void AssertPixError(PixError expected, string payload)
        {
            var e = Assert.Throws<PixException>(() => BrCode.Decode(payload));
            Assert.Equal(expected, e.Error);
            Assert.False(BrCode.TryDecode(payload, out BrCode? code));
            Assert.Null(code);
            Assert.False(BrCode.TryDecode(payload, out _, out BrPayException? error));
            Assert.IsType<PixException>(error);
        }
    }

    [Fact]
    public void TryDecodeSucceeds()
    {
        Assert.True(BrCode.TryDecode(BcbExample, out BrCode? code));
        Assert.NotNull(code);
        Assert.Equal("Fulano de Tal", code.MerchantName);
        Assert.False(BrCode.TryDecode(null, out _));
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("1", 100L)]
    [InlineData("10.5", 1050L)]
    [InlineData("10.50", 1050L)]
    [InlineData("0.01", 1L)]
    [InlineData("007.10", 710L)]
    [InlineData("9999999999.99", 999_999_999_999L)]
    public void ParseAmount(string input, long cents)
    {
        Assert.Null(PixText.ParseAmount(input, out long got));
        Assert.Equal(cents, got);
    }

    [Theory]
    [InlineData("", PixText.AmountSyntax)]
    [InlineData(".5", PixText.AmountSyntax)]
    [InlineData("10.", PixText.AmountSyntax)]
    [InlineData("1.2.3", PixText.AmountSyntax)]
    [InlineData("-1", PixText.AmountSyntax)]
    [InlineData("1e3", PixText.AmountSyntax)]
    [InlineData("10,50", PixText.AmountComma)]
    [InlineData("1.234", PixText.AmountDecimals)]
    [InlineData("12345678901234", PixText.AmountTooLong)]
    public void ParseAmountErrors(string input, string expected) => Assert.Equal(expected, PixText.ParseAmount(input, out _));

    [Theory]
    [InlineData(0L, "0.00")]
    [InlineData(1L, "0.01")]
    [InlineData(10L, "0.10")]
    [InlineData(1050L, "10.50")]
    [InlineData(999_999_999_999L, "9999999999.99")]
    public void FormatAmount(long cents, string expected) => Assert.Equal(expected, PixText.FormatAmount(cents));

    [Theory]
    [InlineData("São João", "Sao Joao")]
    [InlineData("ÁÀÂÃÄ áàâãä", "AAAAA aaaaa")]
    [InlineData("ÉÈÊË éèêë ÍÌÎÏ íìîï", "EEEE eeee IIII iiii")]
    [InlineData("ÓÒÔÕÖ óòôõö", "OOOOO ooooo")]
    [InlineData("ÚÙÛÜ úùûü Çç Ññ", "UUUU uuuu Cc Nn")]
    [InlineData("  plain  ", "plain")]
    public void ToAscii(string input, string expected)
    {
        Assert.Null(PixText.ToAscii(input, out string got));
        Assert.Equal(expected, got);
    }

    [Theory]
    [InlineData("€")]
    [InlineData("a\u0000b")]
    public void ToAsciiRejects(string input) => Assert.NotNull(PixText.ToAscii(input, out _));
}
