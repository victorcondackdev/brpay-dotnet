using BrPay.Documents;

namespace BrPay.Tests.Documents;

public class DocumentTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData("111.444.777-35")]
    public void ValidCpf(string input)
    {
        Assert.Equal(DocumentError.None, Cpf.Check(input));
        Assert.True(Cpf.IsValid(input));
        Cpf.Validate(input);
    }

    [Theory]
    [InlineData("529.982.247-24", DocumentError.CheckDigit)]
    [InlineData("111.111.111-11", DocumentError.Repeated)]
    [InlineData("1234567890", DocumentError.Length)]
    [InlineData("5299822472A", DocumentError.Characters)]
    [InlineData("5299822472é", DocumentError.Length)]
    public void InvalidCpf(string input, DocumentError expected)
    {
        Assert.Equal(expected, Cpf.Check(input));
        var e = Assert.Throws<DocumentException>(() => Cpf.Validate(input));
        Assert.Equal(expected, e.Error);
        Assert.Equal(DocumentKind.Cpf, e.Kind);
    }

    [Fact]
    public void NumericCnpj()
    {
        Assert.True(Cnpj.IsValid("11.222.333/0001-81"));
        Assert.True(Cnpj.IsValid("11222333000181"));
        Assert.Equal(DocumentError.CheckDigit, Cnpj.Check("11.222.333/0001-80"));
        Assert.False(Cnpj.IsAlphanumeric("11.222.333/0001-81"));
    }

    // Example published by the Receita Federal for the alphanumeric CNPJ.
    [Fact]
    public void AlphanumericCnpjFromReceitaFederal()
    {
        const string v = "12.ABC.345/01DE-35";
        Assert.Equal(DocumentError.None, Cnpj.Check(v));
        Assert.True(Cnpj.IsAlphanumeric(v));
        Assert.True(Cnpj.IsValid("12.abc.345/01de-35"));
        Assert.Equal(DocumentError.Characters, Cnpj.Check("12ABC34501DE3A"));
        Assert.Equal((3, 5), Cnpj.ComputeCheckDigits("12ABC34501DE"));
    }

    [Fact]
    public void CpfCheckDigits()
    {
        Assert.Equal((2, 5), Cpf.ComputeCheckDigits("529982247"));
        Assert.Throws<ArgumentException>(() => Cpf.ComputeCheckDigits("12345678"));
        Assert.Throws<ArgumentException>(() => Cnpj.ComputeCheckDigits("12abc34501de"));
    }

    [Fact]
    public void DetectFormatMask()
    {
        Assert.Equal(DocumentKind.Cpf, Document.Detect("52998224725"));
        Assert.Equal(DocumentKind.Cnpj, Document.Detect("11222333000181"));
        Assert.Equal(DocumentKind.Unknown, Document.Detect("123"));
        Assert.Equal("529.982.247-25", Document.Format("52998224725"));
        Assert.Equal("12.ABC.345/01DE-35", Document.Format("12abc34501de35"));
        Assert.Equal("123", Document.Format("1.2.3"));
        Assert.Equal("***.982.247-**", Document.Mask("52998224725"));
        Assert.Equal("12.***.***/01DE-**", Document.Mask("12ABC34501DE35"));
        Assert.Equal("***", Document.Mask("123"));
    }

    [Fact]
    public void CleanKeepsUnknownCharactersForValidation()
    {
        Assert.Equal("12ABC", Document.Clean(" 1.2-a/b c"));
        Assert.Equal("12_3", Document.Clean("12_3"));
    }
}
