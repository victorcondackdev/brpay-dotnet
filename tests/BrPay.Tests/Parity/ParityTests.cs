using System.Globalization;
using BrPay.Boleto;
using BrPay.Documents;
using BrPay.Emv;
using BrPay.Internal;
using BrPay.Pix;

namespace BrPay.Tests.Parity;

/// <summary>
/// Cross-language parity: every vector in <see cref="GoVectors"/> was produced
/// by running the Go implementation (github.com/victorcondackdev/brpay) and the
/// C# port must produce byte-for-byte identical outputs, including error messages.
/// </summary>
public class ParityTests
{
    [Fact]
    public void Crc16MatchesGo()
    {
        var failures = new List<string>();
        foreach ((string input, string hex) in GoVectors.Crc)
        {
            Check(failures, input, hex, Crc16.ComputeHex(input));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void CnpjCheckDigitsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string baseChars, int first, int second) in GoVectors.CnpjCheckDigits)
        {
            (int d1, int d2) = Cnpj.ComputeCheckDigits(baseChars);
            Check(failures, baseChars, first + "," + second, d1 + "," + d2);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void DocumentsMatchGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.Documents)
        {
            string kind = Document.Detect(v.Input) switch
            {
                DocumentKind.Cpf => "CPF",
                DocumentKind.Cnpj => "CNPJ",
                _ => "unknown",
            };
            Check(failures, v.Input + " kind", v.Kind, kind);
            Check(failures, v.Input + " format", v.Formatted, Document.Format(v.Input));
            Check(failures, v.Input + " mask", v.Masked, Document.Mask(v.Input));
            Check(failures, v.Input + " cpf", v.CpfError, Message(Cpf.Check(v.Input)));
            Check(failures, v.Input + " cnpj", v.CnpjError, Message(Cnpj.Check(v.Input)));
            Check(failures, v.Input + " alnum", B(v.Alphanumeric), B(Cnpj.IsAlphanumeric(v.Input)));
        }

        Assert.Empty(failures);

        static string Message(DocumentError e) => e == DocumentError.None ? string.Empty : DocumentException.MessageFor(e);
    }

    [Fact]
    public void TlvMatchesGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.Tlv)
        {
            string parse;
            string validate = string.Empty;
            try
            {
                IReadOnlyList<EmvField> fields = Tlv.Parse(v.Input);
                parse = "ok|" + DescribeFields(fields);
                validate = Capture(() => Tlv.Validate(fields));
            }
            catch (EmvParseException e)
            {
                parse = "err|" + e.Message;
            }

            Check(failures, v.Input + " parse", v.Parse, parse);
            Check(failures, v.Input + " validate", v.Validate, validate);
            Check(failures, v.Input + " verify", v.Verify, Capture(() => Crc16.Verify(v.Input)));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void PixKeysMatchGo()
    {
        var failures = new List<string>();
        foreach ((string input, string expected) in GoVectors.Keys)
        {
            string actual;
            try
            {
                PixKey key = PixKey.Normalize(input);
                actual = key.Type.ToDictName() + "|" + key.Value;
            }
            catch (PixException e)
            {
                actual = "err|" + e.Message;
            }

            Check(failures, input, expected, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void PixDecodeAndLintMatchGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.Pix)
        {
            string decode;
            try
            {
                decode = DescribeBrCode(BrCode.Decode(v.Input));
            }
            catch (BrPayException e)
            {
                decode = "err|" + e.Message;
            }

            Check(failures, v.Input + " decode", v.Decode, decode);
            Check(failures, v.Input + " lint", v.Lint, LintLines(v.Input));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void StaticBuildsMatchGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.StaticBuilds)
        {
            var options = new StaticBrCodeOptions
            {
                Key = v.Key,
                MerchantName = v.Name,
                MerchantCity = v.City,
                Cents = v.Cents,
                TxId = v.TxId,
                Description = v.Description,
                PostalCode = v.PostalCode,
                Mcc = v.Mcc,
            };
            string actual;
            try
            {
                actual = BrCodeBuilder.BuildStatic(options);
            }
            catch (BrPayException e)
            {
                actual = "err|" + e.Message;
            }

            Check(failures, options.ToString(), v.Expected, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void DynamicBuildsMatchGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.DynamicBuilds)
        {
            var options = new DynamicBrCodeOptions
            {
                Url = v.Url,
                MerchantName = v.Name,
                MerchantCity = v.City,
                PostalCode = v.PostalCode,
                Mcc = v.Mcc,
                Reusable = v.Reusable,
            };
            string actual;
            try
            {
                actual = BrCodeBuilder.BuildDynamic(options);
            }
            catch (BrPayException e)
            {
                actual = "err|" + e.Message;
            }

            Check(failures, options.ToString(), v.Expected, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EndToEndIdsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string input, string expected) in GoVectors.EndToEndIds)
        {
            string actual;
            try
            {
                EndToEndId id = EndToEndId.Parse(input);
                actual = "ok|" + id.Ispb + "|" + id.Timestamp.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) + "|" + id.Sequence + "|" + id;
            }
            catch (PixException e)
            {
                actual = "err|" + e.Message;
            }

            Check(failures, input, expected, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EndToEndIdCreationMatchesGoForTheSameRandomBytes()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.EndToEndCreates)
        {
            DateTimeOffset at = DateTimeOffset.Parse(v.At, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            string actual = EndToEndId.Create(v.Ispb, at, SequentialRandom.FromHex(v.RandomHex).Fill);
            Check(failures, v.Ispb + " " + v.At, v.Expected, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void BoletoBuildsMatchGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.BancarioBuilds)
        {
            DateOnly? due = v.Due.Length == 0 ? null : DateOnly.ParseExact(v.Due, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            string actual;
            try
            {
                actual = DescribeBoleto(BoletoCode.CreateBancario(v.Bank, due, v.Amount, v.FreeField));
            }
            catch (BoletoException e)
            {
                actual = "err|" + e.Message;
            }

            Check(failures, v.Bank + " " + v.Due + " " + v.Amount + " " + v.FreeField, v.Expected, actual);
        }

        foreach (var v in GoVectors.ArrecadacaoBuilds)
        {
            string actual;
            try
            {
                actual = DescribeBoleto(BoletoCode.CreateArrecadacao((ArrecadacaoSegment)v.Segment, v.ValueType, v.Value, v.Tail));
            }
            catch (BoletoException e)
            {
                actual = "err|" + e.Message;
            }

            Check(failures, v.Segment + " " + v.ValueType + " " + v.Value + " " + v.Tail, v.Expected, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void BoletoParsingMatchesGo()
    {
        var failures = new List<string>();
        foreach ((string input, string expected) in GoVectors.BoletoParses)
        {
            string actual;
            try
            {
                actual = DescribeBoleto(BoletoCode.Parse(input));
            }
            catch (BoletoException e)
            {
                actual = "err|" + e.Message;
            }

            Check(failures, input, expected, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void BoletoSuggestionsMatchGo()
    {
        var failures = new List<string>();
        foreach ((string input, string expected) in GoVectors.BoletoSuggestions)
        {
            Check(failures, input, expected, string.Join(",", BoletoCode.SuggestFix(input)));
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void DueDatesMatchGo()
    {
        var failures = new List<string>();
        foreach ((int factor, string reference, string expected) in GoVectors.DueDates)
        {
            DateOnly r = DateOnly.ParseExact(reference, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateOnly? got = DueDateFactor.ToDate(factor, r);
            Check(failures, factor + " @ " + reference, expected, got?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
        }

        foreach ((string date, string expected) in GoVectors.Factors)
        {
            string actual;
            try
            {
                actual = DueDateFactor.FromDate(DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
            }
            catch (BoletoException e)
            {
                actual = "err|" + e.Message;
            }

            Check(failures, date, expected, actual);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void DecoderMatchesGo()
    {
        var failures = new List<string>();
        foreach (var v in GoVectors.Decodes)
        {
            DecodeResult r = Decoder.Decode(v.Input);
            string summary = string.Empty;
            if (r.Pix is not null)
            {
                summary = (r.Pix.Key ?? string.Empty) + (r.Pix.Url ?? string.Empty);
            }
            else if (r.Boleto is not null)
            {
                summary = r.Boleto.Barcode;
            }
            else if (r.Document is not null)
            {
                DecodedDocument d = r.Document;
                summary = (d.Kind == DocumentKind.Cpf ? "CPF" : "CNPJ") + "|" + d.Number + "|" + d.Formatted + "|" + d.Masked + "|" + B(d.Alphanumeric);
            }
            else if (r.PixKey is { } key)
            {
                summary = key.Type.ToDictName() + "|" + key.Value;
            }
            else if (r.EndToEndId is { } id)
            {
                summary = id.ToString();
            }

            Check(failures, v.Input + " kind", v.Kind, r.Kind.ToName());
            Check(failures, v.Input + " summary", v.Summary, summary);
            Check(failures, v.Input + " error", v.Error, r.ErrorMessage ?? string.Empty);
            Check(failures, v.Input + " issues", v.Issues, string.Join("\n", r.Issues.Select(i => i.ToString())));
            Check(failures, v.Input + " suggestions", v.Suggestions, string.Join(",", r.Suggestions));
        }

        Assert.Empty(failures);
    }

    internal static string DescribeFields(IReadOnlyList<EmvField> fields) =>
        string.Join(" ", fields.Select(f => f.Children.Count > 0
            ? f.Id + "[" + DescribeFields(f.Children) + "]"
            : f.Id + "=" + AsciiText.Quote(f.Value)));

    internal static string DescribeBrCode(BrCode b)
    {
        string templates = string.Join(",", b.Templates.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Key + "=" + t.Value));
        return string.Join("|", new[]
        {
            "ok", b.KeyType.ToDictName(), b.Key ?? string.Empty, b.Url ?? string.Empty,
            b.Cents.ToString(CultureInfo.InvariantCulture), b.RawAmount ?? string.Empty, b.MerchantName, b.MerchantCity,
            b.TxId ?? string.Empty, b.Mcc, b.PointOfInitiation ?? string.Empty, b.Description ?? string.Empty,
            b.PostalCode ?? string.Empty, B(b.IsStatic), B(b.IsReusable), b.MerchantAccountId, templates,
        });
    }

    internal static string DescribeBoleto(BoletoCode b)
    {
        var parts = new List<string>
        {
            "ok", b.Type.GetDisplayName(), b.Barcode, b.DigitableLine, b.Formatted(), b.AmountCents.ToString(CultureInfo.InvariantCulture),
        };
        if (b.Bancario is { } bk)
        {
            parts.AddRange(new[]
            {
                bk.BankCode, bk.BankName ?? string.Empty, bk.Currency.ToString(CultureInfo.InvariantCulture),
                bk.Factor.ToString(CultureInfo.InvariantCulture), bk.FreeField,
            });
        }

        if (b.Arrecadacao is { } a)
        {
            parts.AddRange(new[]
            {
                ((int)a.Segment).ToString(CultureInfo.InvariantCulture), a.Segment.GetDescription() ?? string.Empty,
                a.ValueType.ToString(CultureInfo.InvariantCulture), B(a.ValueIsReference), a.Modulus.ToString(CultureInfo.InvariantCulture),
                a.CompanyId ?? string.Empty, a.CnpjBase ?? string.Empty, a.FreeField,
            });
        }

        return string.Join("|", parts);
    }

    internal static string LintLines(string payload) => string.Join("\n", Linter.Lint(payload).Select(i => i.ToString()));

    private static string B(bool value) => value ? "true" : "false";

    private static string Capture(Action action)
    {
        try
        {
            action();
            return string.Empty;
        }
        catch (BrPayException e)
        {
            return e.Message;
        }
    }

    private static void Check(List<string> failures, string label, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            failures.Add(label + "\n    go:     " + expected + "\n    dotnet: " + actual);
        }
    }
}

/// <summary>Serves a fixed byte sequence 16 bytes at a time, like Go's io.ReadFull over a bytes.Reader.</summary>
internal sealed class SequentialRandom
{
    private readonly byte[] _data;
    private int _position;

    private SequentialRandom(byte[] data) => _data = data;

    public static SequentialRandom FromHex(string hex) => new(Convert.FromHexString(hex));

    public static SequentialRandom FromBytes(byte[] data) => new(data);

    public void Fill(Span<byte> buffer)
    {
        if (_position + buffer.Length > _data.Length)
        {
            throw new InvalidOperationException("random source exhausted");
        }

        _data.AsSpan(_position, buffer.Length).CopyTo(buffer);
        _position += buffer.Length;
    }
}
