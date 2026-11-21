using BrPay.Pix;
using static BrPay.Tests.Pix.TestPayloads;

namespace BrPay.Tests.Pix;

public class LinterTests
{
    /// <summary>A valid static payload without CRC, built from parts so each test can break exactly one thing.</summary>
    private sealed record Body
    {
        public string Poi { get; init; } = string.Empty;
        public string Account { get; init; } = Obj("26", Obj("00", BrCode.Gui) + Obj("01", "52998224725"));
        public string Mcc { get; init; } = Obj("52", "0000");
        public string Currency { get; init; } = Obj("53", "986");
        public string Amount { get; init; } = string.Empty;
        public string Country { get; init; } = Obj("58", "BR");
        public string Name { get; init; } = Obj("59", "Fulano");
        public string City { get; init; } = Obj("60", "Rio");
        public string Postal { get; init; } = string.Empty;
        public string Additional { get; init; } = Obj("62", Obj("05", "***"));

        public override string ToString() =>
            "000201" + Poi + Account + Mcc + Currency + Amount + Country + Name + City + Postal + Additional;
    }

    private static readonly Body Ok = new();

    private const string Whitespace = "warning: payload has surrounding whitespace or line breaks (often a copy-paste artifact); some apps reject it";

    [Fact]
    public void BcbExampleIsClean() => Assert.Empty(Linter.Lint(BcbExample));

    public static readonly TheoryData<string, string, string[]> Cases = new()
    {
        { "whitespace", WithCrc(Ok.ToString()) + "\n", new[] { Whitespace } },
        { "empty", " ", new[] { Whitespace, "error: payload is empty" } },
        { "wrong crc", BcbExample.Substring(0, BcbExample.Length - 4) + "1D3E", new[] { "error [63]: CRC is \"1D3E\", expected 1D3D (was the payload edited after the CRC was computed?)" } },
        { "lowercase crc", BcbExample.Substring(0, BcbExample.Length - 4) + "1d3d", new[] { "warning [63]: CRC hex digits should be uppercase: 1D3D" } },
        { "amount comma", WithCrc((Ok with { Amount = Obj("54", "10,50") }).ToString()), new[] { "error [54]: amount \"10,50\": uses a comma; the decimal separator must be a dot" } },
        { "amount decimals", WithCrc((Ok with { Amount = Obj("54", "1.234") }).ToString()), new[] { "error [54]: amount \"1.234\": more than 2 decimal places" } },
        { "amount leading zeros", WithCrc((Ok with { Amount = Obj("54", "010.00") }).ToString()), new[] { "warning [54]: amount \"010.00\" has leading zeros; use \"10.00\"" } },
        { "amount zero", WithCrc((Ok with { Amount = Obj("54", "0.00") }).ToString()), new[] { "warning [54]: amount is zero; omit field 54 to let the payer choose the amount" } },
        {
            "long name and city", WithCrc((Ok with { Name = Obj("59", new string('N', 26)), City = Obj("60", "Sao Joao de Meriti") }).ToString()),
            new[] { "error [59]: merchant name is 26 characters, max 25", "error [60]: merchant city is 18 characters, max 15" }
        },
        {
            "accents", WithCrc((Ok with { City = "6008Niterói" }).ToString()),
            new[] { "error [60]: non-ASCII or control character 'ó' at byte 5 of the value; transliterate accents (\"São\" to \"Sao\")" }
        },
        {
            "missing country and currency", WithCrc((Ok with { Country = string.Empty, Currency = string.Empty }).ToString()),
            new[] { "error [53]: missing required field (transaction currency)", "error [58]: missing required field (country code)" }
        },
        {
            "wrong country currency mcc", WithCrc((Ok with { Country = Obj("58", "US"), Currency = Obj("53", "840"), Mcc = Obj("52", "00") }).ToString()),
            new[]
            {
                "error [52]: merchant category code \"00\" must be 4 digits (\"0000\" if unspecified)",
                "error [53]: currency is \"840\", Pix requires \"986\" (BRL)",
                "error [58]: country code is \"US\", want \"BR\"",
            }
        },
        {
            "missing name city mcc", WithCrc((Ok with { Name = string.Empty, City = string.Empty, Mcc = string.Empty }).ToString()),
            new[]
            {
                "error [52]: missing required field (merchant category code)",
                "error [59]: missing required field (merchant name)",
                "error [60]: missing required field (merchant city)",
            }
        },
        { "txid", WithCrc((Ok with { Additional = Obj("62", Obj("05", "PEDIDO-1")) }).ToString()), new[] { "error [62.05]: invalid txid: invalid character '-' at position 6, want [A-Za-z0-9]" } },
        { "txid long", WithCrc((Ok with { Additional = Obj("62", Obj("05", new string('A', 26))) }).ToString()), new[] { "error [62.05]: invalid txid: 26 characters, max 25" } },
        { "no 62", WithCrc((Ok with { Additional = string.Empty }).ToString()), new[] { "warning [62]: missing additional data template; many PSPs expect 62 with subfield 05 (txid, \"***\" for none)" } },
        { "no txid", WithCrc((Ok with { Additional = Obj("62", Obj("01", "x")) }).ToString()), new[] { "warning [62.05]: missing txid; use \"***\" for none" } },
        {
            "key and url", WithCrc((Ok with { Account = Obj("26", Obj("00", BrCode.Gui) + Obj("01", "52998224725") + Obj("25", "https://x.com/qr/1")) }).ToString()),
            new[]
            {
                "error [26]: has both a key (01) and a URL (25); a BR Code is either static or dynamic",
                "error [26.25]: URL must not include the scheme (remove \"https://\")",
            }
        },
        { "no key no url", WithCrc((Ok with { Account = Obj("26", Obj("00", BrCode.Gui)) }).ToString()), new[] { "error [26]: has neither a key (01) nor a URL (25)" } },
        { "not pix", WithCrc((Ok with { Account = Obj("27", Obj("00", "br.com.other")) }).ToString()), new[] { "error [26]: no merchant account template (26–51) with GUI \"br.gov.bcb.pix\"" } },
        {
            "gui case and key form", WithCrc((Ok with { Account = Obj("26", Obj("00", "BR.GOV.BCB.PIX") + Obj("01", "529.982.247-25")) }).ToString()),
            new[]
            {
                "warning [26.00]: GUI \"BR.GOV.BCB.PIX\" should be lowercase \"br.gov.bcb.pix\"",
                "warning [26.01]: key is not in canonical form; use \"52998224725\"",
            }
        },
        { "invalid key", WithCrc((Ok with { Account = Obj("26", Obj("00", BrCode.Gui) + Obj("01", "12345")) }).ToString()), new[] { "error [26.01]: pix: invalid key: not a CPF, CNPJ, e-mail, phone (+55...) or EVP" } },
        {
            "poi and postal", WithCrc((Ok with { Poi = Obj("01", "13"), Postal = Obj("61", "24400-000") }).ToString()),
            new[]
            {
                "error [01]: point of initiation method is \"13\", want \"11\" (reusable) or \"12\" (single use)",
                "warning [61]: postal code \"24400-000\" should be a CEP of 8 digits without hyphen",
            }
        },
        { "structure", WithCrc("000202" + Ok.ToString().Substring(6)), new[] { "error [00]: payload format indicator must be \"01\", got \"02\"" } },
        {
            "no crc", Ok.ToString(),
            new[] { "error [63]: payload must end with the CRC field: \"6304\" followed by 4 hex digits", "error [62]: CRC (63) must be the last field" }
        },
        {
            "syntax", "000201XX01",
            new[] { "error [63]: payload must end with the CRC field: \"6304\" followed by 4 hex digits", "error: emv: byte 6: invalid field ID \"XX\": want 2 digits" }
        },
        {
            "byte-counted length", WithCrc("0002015907Niterói"),
            new[] { "error: emv: byte 17: invalid field ID \"i6\": want 2 digits (the payload contains non-ASCII characters; lengths must count bytes, and BR Codes should be ASCII only)" }
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Lint(string name, string payload, string[] expected)
    {
        Assert.NotNull(name);
        Assert.Equal(expected, Linter.Lint(payload).Select(i => i.ToString()));
    }

    [Fact]
    public void TooLong()
    {
        string p = Ok.ToString();
        foreach (string id in new[] { "80", "81", "82", "83", "84" })
        {
            p += Obj(id, Obj("00", new string('x', 91)));
        }

        LintIssue issue = Assert.Single(Linter.Lint(WithCrc(p)));
        Assert.Equal("error: payload is 595 bytes, max 512", issue.ToString());
        Assert.Equal(LintSeverity.Error, issue.Severity);
        Assert.Equal(string.Empty, issue.Field);
    }

    // Parity vector: the output of the Go ExampleLint.
    [Fact]
    public void MatchesGoExample()
    {
        string payload = "00020126330014br.gov.bcb.pix0111529982247255204000053039865405"
            + "10,005802BR5906Fulano6008Niterói62070503***63041D3D\n";
        Assert.Equal(
            new[]
            {
                Whitespace,
                "error [63]: CRC is \"1D3D\", expected 24A3 (was the payload edited after the CRC was computed?)",
                "error [60]: non-ASCII or control character 'ó' at byte 5 of the value; transliterate accents (\"São\" to \"Sao\")",
                "error [54]: amount \"10,00\": uses a comma; the decimal separator must be a dot",
            },
            Linter.Lint(payload).Select(i => i.ToString()));
    }

    [Fact]
    public void IssueFormatting()
    {
        Assert.Equal("warning: x", new LintIssue(LintSeverity.Warning, string.Empty, "x").ToString());
        Assert.Equal("error [54]: y", new LintIssue(LintSeverity.Error, "54", "y").ToString());
        Assert.Equal("unknown: z", new LintIssue((LintSeverity)0, string.Empty, "z").ToString());
    }
}
