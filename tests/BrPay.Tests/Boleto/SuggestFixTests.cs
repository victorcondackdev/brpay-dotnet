using BrPay.Boleto;

namespace BrPay.Tests.Boleto;

public class SuggestFixTests
{
    private static string Corrupt(string s, int i)
    {
        char[] b = s.ToCharArray();
        b[i] = (char)('0' + ((b[i] - '0' + 3) % 10));
        return new string(b);
    }

    [Fact]
    public void FindsEverySingleTypoInBancarioLine()
    {
        BoletoCode b = BoletoCode.CreateBancario("341", new DateOnly(2026, 12, 10), 15990, "1090100000012345678900001");
        for (int i = 0; i < BoletoCode.BancarioLineLength; i++)
        {
            IReadOnlyList<string> got = BoletoCode.SuggestFix(BoletoCode.FormatLine(Corrupt(b.DigitableLine, i)));
            Assert.Contains(b.DigitableLine, got);
            Assert.True(got.Count <= BoletoCode.MaxSuggestions);
            Assert.All(got, s => Assert.True(BoletoCode.IsValid(s)));
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void FindsSingleTyposInArrecadacaoLine(int valueType)
    {
        BoletoCode b = BoletoCode.CreateArrecadacao(ArrecadacaoSegment.Prefeituras, valueType, 4321, "1234" + new string('5', 25));
        for (int i = 0; i < BoletoCode.ArrecadacaoLineLength; i++)
        {
            string bad = Corrupt(b.DigitableLine, i);

            // Modulo 11 maps remainders 0 and 1 to the same digit, so a few
            // substitutions are undetectable; then there is nothing to fix.
            if (BoletoCode.IsValid(bad))
            {
                continue;
            }

            Assert.Contains(b.DigitableLine, BoletoCode.SuggestFix(bad));
        }
    }

    [Fact]
    public void NoSuggestionForValidOrMalformedInput()
    {
        string two = Corrupt(Corrupt(BancarioTests.HandLine, 2), 15); // fields 1 and 2 both wrong
        foreach (string input in new[] { BancarioTests.HandLine, BancarioTests.HandBarcode, "abc", "123", two })
        {
            Assert.Empty(BoletoCode.SuggestFix(input));
        }
    }

    [Fact]
    public void WrongDacIsTheWorstCaseAndStaysUnderTheCap()
    {
        string bad = BancarioTests.HandLine.Substring(0, 32) + "0" + BancarioTests.HandLine.Substring(33);
        IReadOnlyList<string> got = BoletoCode.SuggestFix(bad);
        Assert.NotEmpty(got);
        Assert.True(got.Count <= BoletoCode.MaxSuggestions);
        Assert.Contains(BancarioTests.HandLine, got);
    }

    // Parity vector: the output of the Go ExampleSuggestFix.
    [Fact]
    public void MatchesGoExample()
    {
        IReadOnlyList<string> got = BoletoCode.SuggestFix("00190.00009 00000.500000 00000.000075 9 10000000010000");
        Assert.Equal(
            new[] { "00190.00009 00009.500000 00000.000075 9 10000000010000", "00190.00009 00000.000000 00000.000075 9 10000000010000" },
            got.Select(BoletoCode.FormatLine));
    }
}
