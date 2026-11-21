using BrPay.Boleto;

namespace BrPay.Tests.Boleto;

public class HybridBoletoTests
{
    private const string SamplePix = "00020101021226580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-4266554400005204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***63041D3D";

    [Fact]
    public void Parse()
    {
        HybridBoleto h = HybridBoleto.Parse(BancarioTests.HandLine, "  " + SamplePix + "\n");
        Assert.Equal(SamplePix, h.PixPayload);
        Assert.Equal(BancarioTests.HandBarcode, h.Boleto.Barcode);
    }

    [Fact]
    public void Errors()
    {
        Assert.Equal(BoletoError.PixPayload, Assert.Throws<BoletoException>(() => HybridBoleto.Parse(BancarioTests.HandLine, "hello")).Error);
        Assert.Equal(BoletoError.Length, Assert.Throws<BoletoException>(() => HybridBoleto.Parse("123", SamplePix)).Error);
    }

    [Fact]
    public void BankNames()
    {
        Assert.Equal("Itaú Unibanco", Banks.GetName("341"));
        Assert.Null(Banks.GetName("999"));
        Assert.Null(Banks.GetName(null));
    }
}
