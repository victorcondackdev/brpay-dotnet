using BrPay.Pix;
using static BrPay.Tests.Pix.TestPayloads;

namespace BrPay.Tests.Pix;

public class BrCodeBuilderTests
{
    private static StaticBrCodeOptions Baseline => new() { Key = "52998224725", MerchantName = "Fulano", MerchantCity = "Rio" };

    [Fact]
    public void StaticMatchesBcbExample()
    {
        string payload = BrCodeBuilder.BuildStatic(new StaticBrCodeOptions
        {
            Key = "123e4567-e12b-12d1-a456-426655440000",
            MerchantName = "Fulano de Tal",
            MerchantCity = "BRASILIA",
        });
        Assert.Equal(BcbExample, payload);
    }

    // Parity vector: the output of the Go Example() for pix.NewStatic.
    [Fact]
    public void StaticWithAccentsMatchesGoExample()
    {
        string payload = BrCodeBuilder.BuildStatic(new StaticBrCodeOptions
        {
            Key = "+55 (21) 98765-4321",
            MerchantName = "Padaria Pão Quente",
            MerchantCity = "Niterói",
            Cents = 1250,
            TxId = "PEDIDO42",
        });
        Assert.Equal("00020126360014br.gov.bcb.pix0114+5521987654321520400005303986540512.505802BR5918Padaria Pao Quente6007Niteroi62120508PEDIDO4263048559", payload);

        BrCode code = BrCode.Decode(payload);
        Assert.Equal("PHONE +5521987654321 12.50 Padaria Pao Quente Niteroi PEDIDO42",
            string.Join(" ", code.KeyType.ToDictName(), code.Key, code.RawAmount, code.MerchantName, code.MerchantCity, code.TxId));
    }

    [Fact]
    public void StaticRoundTrips()
    {
        var full = new StaticBrCodeOptions
        {
            Key = "+55 (21) 98765-4321",
            MerchantName = "  Padaria Pão Quente ",
            MerchantCity = "São Gonçalo",
            Cents = 1050,
            TxId = "PEDIDO42",
            Description = "Café e pão",
            PostalCode = "24400-000",
            Mcc = "5462",
        };
        BrCode b = RoundTrip(full);
        Assert.Equal("+5521987654321", b.Key);
        Assert.Equal(PixKeyType.Phone, b.KeyType);
        Assert.Equal("Padaria Pao Quente", b.MerchantName);
        Assert.Equal("Sao Goncalo", b.MerchantCity);
        Assert.Equal("10.50", b.RawAmount);
        Assert.Equal(1050L, b.Cents);
        Assert.Equal("PEDIDO42", b.TxId);
        Assert.Equal("Cafe e pao", b.Description);
        Assert.Equal("24400000", b.PostalCode);
        Assert.Equal("5462", b.Mcc);

        b = RoundTrip(new StaticBrCodeOptions { Key = "12.ABC.345/01DE-35", MerchantName = "ACME LTDA", MerchantCity = "RIO", Cents = 1 });
        Assert.Equal("12ABC34501DE35", b.Key);
        Assert.Equal(PixKeyType.Cnpj, b.KeyType);
        Assert.Equal("0.01", b.RawAmount);

        b = RoundTrip(new StaticBrCodeOptions { Key = "52998224725", MerchantName = "X", MerchantCity = "Y", Cents = BrCode.MaxCents });
        Assert.Equal("9999999999.99", b.RawAmount);
        Assert.Equal(BrCode.MaxCents, b.Cents);

        static BrCode RoundTrip(StaticBrCodeOptions options)
        {
            string p = BrCodeBuilder.BuildStatic(options);
            Assert.Empty(Linter.Lint(p));
            BrCode b = BrCode.Decode(p);
            Assert.True(b.IsStatic);
            Assert.True(b.IsReusable);
            Assert.Equal("986", b.Currency);
            Assert.Equal("BR", b.CountryCode);
            Assert.Equal(options.Cents, b.Cents);
            return b;
        }
    }

    [Fact]
    public void StaticErrors()
    {
        AssertError(PixError.InvalidKey, Baseline with { Key = "123" });
        AssertError(PixError.InvalidMerchantName, Baseline with { MerchantName = " " });
        AssertError(PixError.InvalidMerchantName, Baseline with { MerchantName = new string('a', 26) });
        AssertError(PixError.InvalidMerchantName, Baseline with { MerchantName = "Loja \U0001F642" });
        AssertError(PixError.InvalidMerchantCity, Baseline with { MerchantCity = "Sao Joao de Meriti" });
        AssertError(PixError.InvalidAmount, Baseline with { Cents = -1 });
        AssertError(PixError.InvalidAmount, Baseline with { Cents = BrCode.MaxCents + 1 });
        AssertError(PixError.InvalidTxId, Baseline with { TxId = "PEDIDO-1" });
        AssertError(PixError.InvalidTxId, Baseline with { TxId = new string('A', 26) });
        AssertError(PixError.InvalidDescription, Baseline with { Description = "tab\there" });
        AssertError(PixError.InvalidDescription, Baseline with { Description = new string('d', 70) });
        AssertError(PixError.InvalidPostalCode, Baseline with { PostalCode = "1234" });
        AssertError(PixError.InvalidPostalCode, Baseline with { PostalCode = "2440000A" });
        AssertError(PixError.InvalidMcc, Baseline with { Mcc = "58A2" });

        BrCodeBuilder.BuildStatic(Baseline);

        // Limits apply after transliteration: exactly 15 characters is fine.
        BrCodeBuilder.BuildStatic(Baseline with { MerchantCity = "Niterói-RJ 2026" });

        static void AssertError(PixError expected, StaticBrCodeOptions options) =>
            Assert.Equal(expected, Assert.Throws<PixException>(() => BrCodeBuilder.BuildStatic(options)).Error);
    }

    [Fact]
    public void Dynamic()
    {
        string p = BrCodeBuilder.BuildDynamic(new DynamicBrCodeOptions
        {
            Url = "HTTPS://pix.example.com/qr/v2/9d36b84f",
            MerchantName = "Loja",
            MerchantCity = "Niterói",
        });
        BrCode b = BrCode.Decode(p);
        Assert.True(b.IsDynamic);
        Assert.False(b.IsReusable);
        Assert.Equal("12", b.PointOfInitiation);
        Assert.Equal("pix.example.com/qr/v2/9d36b84f", b.Url);
        Assert.Equal("Niteroi", b.MerchantCity);
        Assert.Null(b.TxId);
        Assert.Empty(Linter.Lint(p));

        b = BrCode.Decode(BrCodeBuilder.BuildDynamic(new DynamicBrCodeOptions { Url = "pix.example.com/qr/1", MerchantName = "Loja", MerchantCity = "Rio", Reusable = true }));
        Assert.Equal("11", b.PointOfInitiation);
        Assert.True(b.IsReusable);
    }

    [Fact]
    public void DynamicErrors()
    {
        var ok = new DynamicBrCodeOptions { Url = "pix.example.com/qr/1", MerchantName = "Loja", MerchantCity = "Rio" };
        AssertError(PixError.InvalidUrl, ok with { Url = "https://" });
        AssertError(PixError.InvalidUrl, ok with { Url = "http://pix.example.com/qr/1" });
        AssertError(PixError.InvalidUrl, ok with { Url = "pix.example.com/q r" });
        AssertError(PixError.InvalidUrl, ok with { Url = "pix.example.com/" + new string('x', 70) });
        AssertError(PixError.InvalidMerchantName, ok with { MerchantName = string.Empty });
        AssertError(PixError.InvalidMerchantCity, ok with { MerchantCity = string.Empty });
        AssertError(PixError.InvalidMcc, ok with { Mcc = "1" });

        static void AssertError(PixError expected, DynamicBrCodeOptions options) =>
            Assert.Equal(expected, Assert.Throws<PixException>(() => BrCodeBuilder.BuildDynamic(options)).Error);
    }
}
