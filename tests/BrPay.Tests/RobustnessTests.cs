using System.Text;
using BrPay.Boleto;
using BrPay.Documents;
using BrPay.Emv;
using BrPay.Pix;

namespace BrPay.Tests;

/// <summary>
/// The .NET analogue of the Go fuzz targets: a deterministic stream of random
/// and mutated inputs that must never make the library throw anything other
/// than its documented <see cref="BrPayException"/> subclasses, and that must
/// preserve the same invariants the Go fuzzers check.
/// </summary>
public class RobustnessTests
{
    private const int Cases = 20_000;

    private static readonly string[] Seeds =
    {
        "00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-426655440000"
            + "5204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***63041D3D",
        "00020101021226420014br.gov.bcb.pix2520pix.example.com/qr/152040000530398654041.005802BR5903Loj6002RJ62070503***6304ABCD",
        "000201",
        "00190.00009 00000.000000 00000.000075 9 10000000010000",
        "00199100000000100000000000000000000000000007",
        "82650000000-3 10000001000-7 00000000000-0 00000000000-0",
        "00190.00009 00000.500000 00000.000075 9 10000000010000",
        "529.982.247-25",
        "12.ABC.345/01DE-35",
        "fulano@example.com",
        "+55 (21) 98765-4321",
        "123e4567-e12b-12d1-a456-426655440000",
        "E60701190202611211430abcdeFGHIJ1",
        string.Empty,
    };

    private const string Alphabet = "0123456789ABCDEFabcdefXYZxyz.-/ @+:,*\t\r\n";

    // Non-ASCII, multi-byte, lone surrogates, NUL, NBSP, LINE SEPARATOR and U+FFFD, built from code points.
    private static readonly string[] Exotic =
        new[] { 0x00E3, 0x00F3, 0x20AC, 0xD800, 0xDC00, 0x0000, 0x00A0, 0x2028, 0xFFFD }
            .Select(c => ((char)c).ToString())
            .Append(char.ConvertFromUtf32(0x1F642))
            .ToArray();

    [Fact]
    public void RandomInputsOnlyThrowDocumentedExceptions()
    {
        var random = new Random(20260221);
        var failures = new List<string>();
        for (int i = 0; i < Cases && failures.Count < 20; i++)
        {
            string input = Next(random, i);
            try
            {
                Exercise(input);
            }
            catch (Exception e)
            {
                failures.Add(Escape(input) + " -> " + e.GetType().Name + ": " + e.Message);
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void BuildersOnlyThrowDocumentedExceptions()
    {
        var random = new Random(42);
        string[] keys = { "52998224725", "a@b.co", "+5521987654321", "123e4567-e12b-12d1-a456-426655440000", "x", string.Empty };
        var failures = new List<string>();
        for (int i = 0; i < 5_000; i++)
        {
            var options = new StaticBrCodeOptions
            {
                Key = random.Next(3) == 0 ? Next(random, i) : keys[random.Next(keys.Length)],
                MerchantName = Next(random, i),
                MerchantCity = Next(random, i + 1),
                Cents = random.Next(4) == 0 ? random.NextInt64(-10, BrCode.MaxCents + 10) : random.Next(0, 100_000),
                TxId = random.Next(2) == 0 ? null : Next(random, i + 2),
                Description = random.Next(2) == 0 ? null : Next(random, i + 3),
                PostalCode = random.Next(2) == 0 ? null : "24400-000",
            };
            try
            {
                string payload = BrCodeBuilder.BuildStatic(options);

                // Anything the builder produces must decode back and lint clean.
                BrCode code = BrCode.Decode(payload);
                if (code.Cents != options.Cents || Linter.Lint(payload).Count != 0)
                {
                    failures.Add("invariant: " + options);
                }
            }
            catch (BrPayException)
            {
            }
            catch (Exception e)
            {
                failures.Add(options + " -> " + e.GetType().Name + ": " + e.Message);
            }
        }

        Assert.Empty(failures);
    }

    private static void Exercise(string s)
    {
        // Never-throwing entry points.
        DecodeResult result = Decoder.Decode(s);
        IReadOnlyList<LintIssue> issues = Linter.Lint(s);
        _ = Decoder.Detect(s);
        _ = Document.Detect(s);
        _ = Document.Format(s);
        _ = Document.Mask(s);
        _ = Cpf.Check(s);
        _ = Cnpj.Check(s);
        _ = Cnpj.IsAlphanumeric(s);
        _ = Crc16.ComputeHex(s);
        _ = Crc16.IsValid(s);
        _ = PixKey.Detect(s);
        _ = EndToEndId.TryParse(s, out _);
        IReadOnlyList<string> suggestions = BoletoCode.SuggestFix(s);

        Require(result.Kind == Decoder.Detect(s), "Decode kind differs from Detect", s);
        int set = (result.Pix is null ? 0 : 1) + (result.Boleto is null ? 0 : 1) + (result.Document is null ? 0 : 1)
            + (result.PixKey is null ? 0 : 1) + (result.EndToEndId is null ? 0 : 1);
        Require(result.Success ? set == 1 : set == 0, "payload count inconsistent with error", s);
        Require(result.Kind == InputKind.Pix || result.Issues.Count == 0, "issues outside pix", s);
        Require(result.Kind == InputKind.Boleto || result.Suggestions.Count == 0, "suggestions outside boleto", s);

        // A successful TLV parse of ASCII input must re-encode to the identical input.
        if (Tlv.TryParse(s, out IReadOnlyList<EmvField>? fields))
        {
            if (IsAscii(s))
            {
                Require(Tlv.Encode(fields) == s, "TLV round trip", s);
            }

            try
            {
                Tlv.Validate(fields);
            }
            catch (EmvParseException)
            {
            }
        }

        if (BrCode.TryDecode(s, out BrCode? code, out _))
        {
            Require(code.IsStatic != code.IsDynamic, "static xor dynamic", s);
            Require(code.Cents >= 0 && code.Cents <= BrCode.MaxCents, "cents range", s);
            Require(!issues.Any(i => i.Field == "63" && i.Severity == LintSeverity.Error), "decoded but lint reports a CRC error", s);
        }

        if (BoletoCode.TryParse(s, out BoletoCode? boleto))
        {
            Require(suggestions.Count == 0, "suggestions for a valid code", s);
            foreach (string form in new[] { boleto.Barcode, boleto.DigitableLine, boleto.Formatted() })
            {
                BoletoCode again = BoletoCode.Parse(form);
                Require(again.Barcode == boleto.Barcode && again.DigitableLine == boleto.DigitableLine, "boleto round trip", s);
            }

            Require(BoletoCode.BarcodeToLine(boleto.Barcode) == boleto.DigitableLine, "BarcodeToLine", s);
            Require(BoletoCode.LineToBarcode(boleto.DigitableLine) == boleto.Barcode, "LineToBarcode", s);
            _ = boleto.Bancario?.DueDate();
        }

        if (PixKey.TryNormalize(s, out PixKey key))
        {
            // Normalization is idempotent and preserves the type.
            Require(PixKey.Normalize(key.Value) == key, "key normalization idempotent", s);
        }

        // Throwing entry points may only throw library exceptions (caught by the caller otherwise).
        Swallow(() => BrCode.Decode(s));
        Swallow(() => BoletoCode.Parse(s));
        Swallow(() => HybridBoleto.Parse(s, s));
        Swallow(() => EndToEndId.Parse(s));
        Swallow(() => Tlv.ParseTemplate(s));
        Swallow(() => Crc16.Verify(s));
    }

    private static void Swallow(Action action)
    {
        try
        {
            action();
        }
        catch (BrPayException)
        {
        }
    }

    private static void Swallow(Func<object> action) => Swallow(() => { _ = action(); });

    private static void Require(bool condition, string what, string input)
    {
        if (!condition)
        {
            throw new InvalidOperationException("invariant violated (" + what + ") for " + Escape(input));
        }
    }

    private static string Next(Random random, int i)
    {
        if (i % 3 != 0)
        {
            // Mutate a seed with one to three edits.
            var b = new StringBuilder(Seeds[random.Next(Seeds.Length)]);
            for (int n = random.Next(1, 4); n > 0; n--)
            {
                string piece = random.Next(8) == 0 ? Exotic[random.Next(Exotic.Length)] : Alphabet[random.Next(Alphabet.Length)].ToString();
                int at = b.Length == 0 ? 0 : random.Next(b.Length);
                switch (random.Next(3))
                {
                    case 0 when b.Length > 0:
                        b.Remove(at, 1).Insert(at, piece);
                        break;
                    case 1:
                        b.Insert(at, piece);
                        break;
                    default:
                        if (b.Length > 0)
                        {
                            b.Remove(at, 1);
                        }

                        break;
                }
            }

            return b.ToString();
        }

        // Fully random string.
        int length = random.Next(0, 120);
        var s = new StringBuilder(length);
        for (int k = 0; k < length; k++)
        {
            s.Append(random.Next(10) == 0 ? Exotic[random.Next(Exotic.Length)] : Alphabet[random.Next(Alphabet.Length)].ToString());
        }

        return s.ToString();
    }

    private static bool IsAscii(string s) => s.All(c => c < 0x80);

    private static string Escape(string s)
    {
        var b = new StringBuilder();
        foreach (char c in s)
        {
            if (c >= 0x20 && c < 0x7F)
            {
                b.Append(c);
            }
            else
            {
                b.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return b.ToString();
    }
}
