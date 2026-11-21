using System.Text.Json;
using BrPay.Boleto;
using BrPay.Documents;
using BrPay.Emv;
using BrPay.Pix;

namespace BrPay.Tests;

public class DecoderTests
{
    private const string StaticPix = "00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-426655440000"
        + "5204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***63041D3D";
    private const string BbLine = "00190.00009 00000.000000 00000.000075 9 10000000010000";
    private const string BbBarcode = "00199100000000100000000000000000000000000007";
    private const string BbLineTypo = "00190.00009 00000.500000 00000.000075 9 10000000010000";
    private const string ArrLine = "82650000000-3 10000001000-7 00000000000-0 00000000000-0";
    private const string ArrBarcode = "82650000000100000010000000000000000000000000";
    private const string E2e = "E60701190202611211430abcdeFGHIJ1";
    private const string Evp = "123e4567-e12b-12d1-a456-426655440000";

    [Theory]
    [InlineData("", InputKind.Unknown)]
    [InlineData(" \n\t", InputKind.Unknown)]
    [InlineData(StaticPix, InputKind.Pix)]
    [InlineData(StaticPix + "\n", InputKind.Pix)]
    [InlineData("000201garbage", InputKind.Pix)]
    [InlineData(BbLine, InputKind.Boleto)]
    [InlineData(BbBarcode, InputKind.Boleto)]
    [InlineData(BbLineTypo, InputKind.Boleto)]
    [InlineData(ArrLine, InputKind.Boleto)]
    [InlineData(ArrBarcode, InputKind.Boleto)]
    [InlineData("0019910000000010000000000000000000000000000", InputKind.Unknown)]
    [InlineData("529.982.247-25", InputKind.Document)]
    [InlineData("52998224725", InputKind.Document)]
    [InlineData("52998224724", InputKind.Document)]
    [InlineData("11.222.333/0001-81", InputKind.Document)]
    [InlineData("12.ABC.345/01DE-35", InputKind.Document)]
    [InlineData("12.abc.345/01de-35", InputKind.Document)]
    [InlineData(E2e, InputKind.EndToEndId)]
    [InlineData("fulano@example.com", InputKind.PixKey)]
    [InlineData("+55 (21) 98765-4321", InputKind.PixKey)]
    [InlineData(Evp, InputKind.PixKey)]
    [InlineData("123E4567-E12B-12D1-A456-426655440000", InputKind.PixKey)]
    [InlineData("hello", InputKind.Unknown)]
    [InlineData("123456789012", InputKind.Unknown)]
    public void Detect(string input, InputKind expected) => Assert.Equal(expected, Decoder.Detect(input));

    // A linha digitável of bank "000", currency 2, starts with "000201", the
    // Pix payload format indicator. A valid boleto must win.
    [Fact]
    public void ValidBoletoWinsOverPixPrefix()
    {
        string? line = null;
        for (int dac = 0; dac < 10 && line is null; dac++)
        {
            string code = "0002" + dac + "1000" + new string('0', 10) + "01" + new string('0', 23);
            if (BoletoCode.IsValid(code))
            {
                line = BoletoCode.BarcodeToLine(code);
            }
        }

        Assert.NotNull(line);
        Assert.StartsWith("000201", line, StringComparison.Ordinal);
        Assert.Equal(InputKind.Boleto, Decoder.Detect(line));

        // With a broken check digit it is no longer a valid boleto, and the Pix prefix takes over.
        string broken = line.Substring(0, 46) + (char)('0' + ((line[46] - '0' + 1) % 10));
        Assert.Equal(InputKind.Pix, Decoder.Detect(broken));
    }

    [Fact]
    public void DecodePix()
    {
        DecodeResult r = Decoder.Decode(StaticPix);
        Assert.True(r.Success);
        Assert.Equal(InputKind.Pix, r.Kind);
        Assert.Equal(Evp, r.Pix?.Key);
        Assert.Empty(r.Issues);
        Assert.Null(r.Boleto);
        Assert.Null(r.Document);
        Assert.Null(r.PixKey);
        Assert.Null(r.EndToEndId);
    }

    [Fact]
    public void DecodePixLintsEvenOnFailure()
    {
        DecodeResult r = Decoder.Decode(StaticPix.Replace("63041D3D", "6304FFFF", StringComparison.Ordinal));
        Assert.IsType<EmvCrcException>(r.Error);
        Assert.Equal(InputKind.Pix, r.Kind);
        Assert.Null(r.Pix);
        Assert.NotEmpty(r.Issues);
        Assert.Equal("63", r.Issues[0].Field);
        Assert.Equal(LintSeverity.Error, r.Issues[0].Severity);

        r = Decoder.Decode("000201269");
        Assert.IsType<EmvParseException>(r.Error);
        Assert.NotEmpty(r.Issues);
    }

    [Fact]
    public void DecodePixWhitespaceWarning()
    {
        DecodeResult r = Decoder.Decode(StaticPix + "\r\n");
        Assert.NotNull(r.Pix);
        LintIssue issue = Assert.Single(r.Issues);
        Assert.Equal(LintSeverity.Warning, issue.Severity);
    }

    [Theory]
    [InlineData(BbLine)]
    [InlineData(BbBarcode)]
    [InlineData(ArrLine)]
    [InlineData(ArrBarcode)]
    public void DecodeBoleto(string input)
    {
        DecodeResult r = Decoder.Decode(input);
        Assert.True(r.Success);
        Assert.Equal(InputKind.Boleto, r.Kind);
        Assert.NotNull(r.Boleto);
        Assert.Empty(r.Suggestions);
    }

    [Fact]
    public void DecodeBoletoSuggestions()
    {
        DecodeResult r = Decoder.Decode(BbLineTypo);
        var e = Assert.IsType<CheckDigitException>(r.Error);
        Assert.Equal(2, e.Field);
        Assert.Null(r.Boleto);
        Assert.Contains("00190000090000000000000000000075910000000010000", r.Suggestions);

        // A barcode has no per-field check digits to locate a typo with.
        r = Decoder.Decode("00199100000000100000000000000000000000000008");
        Assert.IsType<CheckDigitException>(r.Error);
        Assert.Empty(r.Suggestions);
    }

    [Theory]
    [InlineData("529.982.247-25", DocumentKind.Cpf, "529.982.247-25", false)]
    [InlineData("52998224725", DocumentKind.Cpf, "529.982.247-25", false)]
    [InlineData("11.222.333/0001-81", DocumentKind.Cnpj, "11.222.333/0001-81", false)]
    [InlineData("12abc34501de35", DocumentKind.Cnpj, "12.ABC.345/01DE-35", true)]
    public void DecodeDocument(string input, DocumentKind kind, string formatted, bool alphanumeric)
    {
        DecodeResult r = Decoder.Decode(input);
        Assert.True(r.Success);
        DecodedDocument? d = r.Document;
        Assert.NotNull(d);
        Assert.Equal(kind, d.Kind);
        Assert.Equal(formatted, d.Formatted);
        Assert.Equal(alphanumeric, d.Alphanumeric);
        Assert.NotEmpty(d.Masked);
        Assert.DoesNotContain('.', d.Number);
    }

    [Theory]
    [InlineData("52998224724", DocumentError.CheckDigit)]
    [InlineData("111.111.111-11", DocumentError.Repeated)]
    [InlineData("11.222.333/0001-82", DocumentError.CheckDigit)]
    public void DecodeInvalidDocument(string input, DocumentError expected)
    {
        DecodeResult r = Decoder.Decode(input);
        Assert.Equal(InputKind.Document, r.Kind);
        Assert.Null(r.Document);
        Assert.Equal(expected, Assert.IsType<DocumentException>(r.Error).Error);
    }

    [Fact]
    public void DecodePixKeyAndEndToEndId()
    {
        DecodeResult r = Decoder.Decode("+55 (21) 98765-4321");
        Assert.Equal(PixKeyType.Phone, r.PixKey?.Type);
        Assert.Equal("+5521987654321", r.PixKey?.Value);

        r = Decoder.Decode(Evp.ToUpperInvariant());
        Assert.Equal(PixKeyType.Evp, r.PixKey?.Type);
        Assert.Equal(Evp, r.PixKey?.Value);

        r = Decoder.Decode("not-an-email@");
        Assert.Equal(InputKind.PixKey, r.Kind);
        Assert.Equal(PixError.InvalidKey, Assert.IsType<PixException>(r.Error).Error);

        r = Decoder.Decode(E2e);
        Assert.Equal("60701190", r.EndToEndId?.Ispb);

        r = Decoder.Decode("E60701190202613211430abcdeFGHIJ1");
        Assert.Equal(PixError.InvalidEndToEndId, Assert.IsType<PixException>(r.Error).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("123")]
    public void DecodeUnrecognized(string input)
    {
        DecodeResult r = Decoder.Decode(input);
        Assert.Equal(InputKind.Unknown, r.Kind);
        Assert.IsType<UnrecognizedInputException>(r.Error);
        Assert.Equal("brpay: unrecognized input", r.ErrorMessage);
        Assert.False(r.Success);
    }

    [Fact]
    public void KindNames()
    {
        Assert.Equal("unknown", InputKind.Unknown.ToName());
        Assert.Equal("pix", InputKind.Pix.ToName());
        Assert.Equal("boleto", InputKind.Boleto.ToName());
        Assert.Equal("document", InputKind.Document.ToName());
        Assert.Equal("pix_key", InputKind.PixKey.ToName());
        Assert.Equal("end_to_end_id", InputKind.EndToEndId.ToName());
        Assert.Equal("unknown", ((InputKind)99).ToName());
    }

    // What an ASP.NET Core minimal API returns: the result must serialize, errors included.
    [Theory]
    [InlineData(StaticPix)]
    [InlineData(BbLineTypo)]
    [InlineData("12.ABC.345/01DE-35")]
    [InlineData(E2e)]
    [InlineData("fulano@example.com")]
    [InlineData("hello")]
    public void ResultSerializesToJson(string input)
    {
        string json = JsonSerializer.Serialize(Decoder.Decode(input));
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("Kind").ValueKind);
        Assert.False(doc.RootElement.TryGetProperty("Error", out _));
    }
}
