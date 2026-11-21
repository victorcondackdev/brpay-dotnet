using BrPay.Boleto;

namespace BrPay.Tests.Boleto;

public class CheckDigitsTests
{
    [Theory]
    [InlineData("0", '0')]
    [InlineData("5", '9')] // 5*2 = 10 -> 1+0 = 1 -> (10-1)%10 = 9
    [InlineData("7992739871", '3')] // classic Luhn example, same weighting
    [InlineData("341910901", '6')]
    [InlineData("0000000000", '0')]
    public void Mod10(string digits, char expected) => Assert.Equal(expected, CheckDigits.Mod10(digits));

    [Theory]
    [InlineData("0", '1')] // 11 - 0 = 11 -> 1
    [InlineData("5", '1')] // 10 % 11 = 10 -> 11 - 10 = 1
    [InlineData("6", '1')] // 12 % 11 = 1 -> 10 -> 1
    [InlineData("1", '9')]
    [InlineData("10", '8')]
    public void Mod11Bancario(string digits, char expected) => Assert.Equal(expected, CheckDigits.Mod11Bancario(digits));

    [Theory]
    [InlineData("0", '0')]
    [InlineData("6", '0')]
    [InlineData("5", '1')]
    [InlineData("1", '9')]
    [InlineData("10", '8')]
    [InlineData("100000000", '9')] // weights cycle back to 2 after 9
    public void Mod11Arrecadacao(string digits, char expected) => Assert.Equal(expected, CheckDigits.Mod11Arrecadacao(digits));

    [Fact]
    public void RejectsNonDigits() => Assert.Throws<ArgumentException>(() => CheckDigits.Mod10("12a"));
}
