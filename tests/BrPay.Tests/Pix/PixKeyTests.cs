using BrPay.Pix;

namespace BrPay.Tests.Pix;

public class PixKeyTests
{
    [Theory]
    [InlineData("529.982.247-25", "52998224725", PixKeyType.Cpf)]
    [InlineData(" 52998224725 ", "52998224725", PixKeyType.Cpf)]
    [InlineData("11.222.333/0001-81", "11222333000181", PixKeyType.Cnpj)]
    [InlineData("12.abc.345/01de-35", "12ABC34501DE35", PixKeyType.Cnpj)]
    [InlineData("Fulano.Tal@Example.COM", "fulano.tal@example.com", PixKeyType.Email)]
    [InlineData("a+tag@sub.example.com.br", "a+tag@sub.example.com.br", PixKeyType.Email)]
    [InlineData("+55 (21) 98765-4321", "+5521987654321", PixKeyType.Phone)]
    [InlineData("+552133334444", "+552133334444", PixKeyType.Phone)]
    [InlineData("123E4567-E12B-12D1-A456-426655440000", "123e4567-e12b-12d1-a456-426655440000", PixKeyType.Evp)]
    public void Normalize(string input, string expected, PixKeyType type)
    {
        PixKey key = PixKey.Normalize(input);
        Assert.Equal(expected, key.Value);
        Assert.Equal(type, key.Type);
        Assert.Equal(expected, key.ToString());
        Assert.Equal(type, PixKey.Detect(input));

        // Normalization is idempotent and preserves the type.
        Assert.Equal(key, PixKey.Normalize(key.Value));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("529.982.247-24", "CPF")]
    [InlineData("21987654321", "phone keys must start with +55")]
    [InlineData("11.222.333/0001-80", "CNPJ")]
    [InlineData("12345", "not a CPF")]
    [InlineData("a@b@example.com", "more than one @")]
    [InlineData("@example.com", "local part")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa@example.com", "local part")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa@example.com", "longer than 77")]
    [InlineData(".a@example.com", "misplaced dot")]
    [InlineData("a..b@example.com", "misplaced dot")]
    [InlineData("a.@example.com", "misplaced dot")]
    [InlineData("a b@example.com", "invalid character")]
    [InlineData("joão@example.com", "invalid character")]
    [InlineData("a@localhost", "must contain a dot")]
    [InlineData("a@-example.com", "invalid e-mail domain")]
    [InlineData("a@example..com", "invalid e-mail domain")]
    [InlineData("a@exa_mple.com", "invalid character")]
    [InlineData("+1 212 555 0100", "Brazilian")]
    [InlineData("+", "Brazilian")]
    [InlineData("+55 21 9876-543", "area code and 8 or 9")]
    [InlineData("+55 21 98765-43210", "area code and 8 or 9")]
    [InlineData("+55 01 98765-4321", "area code")]
    [InlineData("+55 21 88765-4321", "must start with 9")]
    [InlineData("+55 21 98765#4321", "invalid character")]
    [InlineData("+55123456789012345678", "too long")]
    [InlineData("123e4567-e12b-12d1-a456-42665544000g", "hexadecimal")]
    public void Invalid(string input, string message)
    {
        var e = Assert.Throws<PixException>(() => PixKey.Normalize(input));
        Assert.Equal(PixError.InvalidKey, e.Error);
        Assert.Contains(message, e.Message, StringComparison.Ordinal);
        Assert.False(PixKey.TryNormalize(input, out PixKey key));
        Assert.Equal(default(PixKey), key);
        Assert.Equal(PixKeyType.Unknown, PixKey.Detect(input));
    }

    [Fact]
    public void DictNames()
    {
        Assert.Equal("UNKNOWN", PixKeyType.Unknown.ToDictName());
        Assert.Equal("CPF", PixKeyType.Cpf.ToDictName());
        Assert.Equal("CNPJ", PixKeyType.Cnpj.ToDictName());
        Assert.Equal("EMAIL", PixKeyType.Email.ToDictName());
        Assert.Equal("PHONE", PixKeyType.Phone.ToDictName());
        Assert.Equal("EVP", PixKeyType.Evp.ToDictName());
        Assert.Equal("UNKNOWN", ((PixKeyType)99).ToDictName());
    }
}
