using System.Text;
using BrPay.Emv;
using BrPay.Internal;

namespace BrPay.Pix;

/// <summary>Classifies a <see cref="LintIssue"/>.</summary>
public enum LintSeverity
{
    /// <summary>Valid but likely to cause trouble with some PSPs, or not in canonical form.</summary>
    Warning = 1,

    /// <summary>Violates the specification; banking apps are expected to reject the code.</summary>
    Error = 2,
}

/// <summary>A problem found by <see cref="Linter.Lint"/>.</summary>
public readonly record struct LintIssue
{
    /// <summary>Initializes an issue.</summary>
    /// <param name="severity">How serious the issue is.</param>
    /// <param name="field">The field ID, as a dotted path for subfields, or empty for the payload as a whole.</param>
    /// <param name="message">What is wrong and, where possible, how to fix it.</param>
    public LintIssue(LintSeverity severity, string field, string message)
    {
        Severity = severity;
        Field = field;
        Message = message;
    }

    /// <summary>How serious the issue is.</summary>
    public LintSeverity Severity { get; }

    /// <summary>The field ID, as a dotted path for subfields (<c>"26.01"</c>, <c>"62.05"</c>), or empty for the payload as a whole.</summary>
    public string Field { get; }

    /// <summary>Explains the problem and, where possible, the fix.</summary>
    public string Message { get; }

    /// <summary>Formats the issue as <c>error [54]: message</c>, identical to the Go implementation.</summary>
    /// <returns>The formatted issue.</returns>
    public override string ToString()
    {
        string severity = Severity switch
        {
            LintSeverity.Warning => "warning",
            LintSeverity.Error => "error",
            _ => "unknown",
        };
        return string.IsNullOrEmpty(Field) ? severity + ": " + Message : severity + " [" + Field + "]: " + Message;
    }
}

/// <summary>
/// Reports every problem it can find in a BR Code payload without stopping
/// at the first one. It targets the mistakes that make banking apps reject a
/// code: a stale CRC, an amount written with a comma, a merchant name that is
/// too long, accents, stray white space from copy-paste and so on.
/// </summary>
public static class Linter
{
    /// <summary>
    /// Checks a payload and returns every issue found, in a stable order
    /// (identical to the Go implementation). It never throws for any
    /// input: when the TLV cannot be parsed, it reports the positional parse
    /// error (and the CRC check, which does not need parsing) and stops there.
    /// </summary>
    /// <param name="payload">The payload, as pasted by a user.</param>
    /// <returns>The issues; empty when the payload is clean.</returns>
    public static IReadOnlyList<LintIssue> Lint(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var issues = new List<LintIssue>();
        string p = AsciiText.TrimSpace(payload);
        if (p.Length != payload.Length)
        {
            Warn(issues, string.Empty, "payload has surrounding whitespace or line breaks (often a copy-paste artifact); some apps reject it");
        }

        if (p.Length == 0)
        {
            Error(issues, string.Empty, "payload is empty");
            return issues;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(p);
        if (bytes.Length > BrCode.MaxPayloadLength)
        {
            Error(issues, string.Empty, "payload is " + AsciiText.Str(bytes.Length) + " bytes, max " + AsciiText.Str(BrCode.MaxPayloadLength));
        }

        CheckCrc(issues, bytes);

        IReadOnlyList<EmvField>? fields = Tlv.ParseCore(p, expand: true, out EmvParseException? parseError);
        if (fields is null)
        {
            string message = parseError?.Message ?? "emv: malformed TLV";
            if (!AsciiText.IsPrintable(p))
            {
                message += " (the payload contains non-ASCII characters; lengths must count bytes, and BR Codes should be ASCII only)";
            }

            Error(issues, parseError?.FieldId ?? string.Empty, message);
            return issues;
        }

        EmvParseException? structureError = Tlv.ValidateCore(fields);
        if (structureError is not null)
        {
            Error(issues, structureError.FieldId, structureError.Reason);
        }

        CheckText(issues, fields, string.Empty);
        CheckFields(issues, fields);
        return issues;
    }

    private static void Error(List<LintIssue> issues, string field, string message) =>
        issues.Add(new LintIssue(LintSeverity.Error, field, message));

    private static void Warn(List<LintIssue> issues, string field, string message) =>
        issues.Add(new LintIssue(LintSeverity.Warning, field, message));

    private static void CheckCrc(List<LintIssue> issues, byte[] p)
    {
        int n = p.Length;
        if (n < 8 || !p.AsSpan(n - 8, 4).SequenceEqual("6304"u8))
        {
            Error(issues, "63", "payload must end with the CRC field: \"6304\" followed by 4 hex digits");
            return;
        }

        string got = Encoding.UTF8.GetString(p, n - 4, 4);
        string want = Crc16.ToHex(Crc16.Compute(p.AsSpan(0, n - 4)));
        if (!AsciiText.EqualsIgnoreCase(got, want))
        {
            Error(issues, "63", "CRC is " + AsciiText.QuoteUtf8(p.AsSpan(n - 4)) + ", expected " + want + " (was the payload edited after the CRC was computed?)");
        }
        else if (got != want)
        {
            Warn(issues, "63", "CRC hex digits should be uppercase: " + want);
        }
    }

    // Reports values that are not printable ASCII.
    private static void CheckText(List<LintIssue> issues, IReadOnlyList<EmvField> fields, string parent)
    {
        foreach (EmvField f in fields)
        {
            string path = parent.Length == 0 ? f.Id : parent + "." + f.Id;
            if (f.Children.Count > 0)
            {
                CheckText(issues, f.Children, path);
                continue;
            }

            string v = f.Value;
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if (c < 0x20 || c > 0x7E)
                {
                    // Every character before i is printable ASCII, so i is also the byte offset.
                    Error(issues, path, "non-ASCII or control character " + AsciiText.QuoteRune(AsciiText.RuneAt(v, i)) + " at byte " + AsciiText.Str(i) + " of the value; transliterate accents (\"São\" to \"Sao\")");
                    break;
                }
            }
        }
    }

    private static void CheckFields(List<LintIssue> issues, IReadOnlyList<EmvField> fields)
    {
        EmvField? poi = Tlv.Find(fields, "01");
        if (poi is not null && poi.Value != BrCode.InitiationReusable && poi.Value != BrCode.InitiationSingleUse)
        {
            Error(issues, "01", "point of initiation method is " + AsciiText.Quote(poi.Value) + ", want \"11\" (reusable) or \"12\" (single use)");
        }

        CheckAccount(issues, fields);

        EmvField? f = Require(issues, fields, "52", "merchant category code");
        if (f is not null && (f.ByteLength != 4 || !AsciiText.IsDigits(f.Value)))
        {
            Error(issues, "52", "merchant category code " + AsciiText.Quote(f.Value) + " must be 4 digits (\"0000\" if unspecified)");
        }

        f = Require(issues, fields, "53", "transaction currency");
        if (f is not null && f.Value != "986")
        {
            Error(issues, "53", "currency is " + AsciiText.Quote(f.Value) + ", Pix requires \"986\" (BRL)");
        }

        f = Tlv.Find(fields, "54");
        if (f is not null)
        {
            CheckAmount(issues, f.Value);
        }

        f = Require(issues, fields, "58", "country code");
        if (f is not null && f.Value != "BR")
        {
            Error(issues, "58", "country code is " + AsciiText.Quote(f.Value) + ", want \"BR\"");
        }

        f = Require(issues, fields, "59", "merchant name");
        if (f is not null && f.ByteLength > BrCode.MaxMerchantNameLength)
        {
            Error(issues, "59", "merchant name is " + AsciiText.Str(f.ByteLength) + " characters, max " + AsciiText.Str(BrCode.MaxMerchantNameLength));
        }

        f = Require(issues, fields, "60", "merchant city");
        if (f is not null && f.ByteLength > BrCode.MaxMerchantCityLength)
        {
            Error(issues, "60", "merchant city is " + AsciiText.Str(f.ByteLength) + " characters, max " + AsciiText.Str(BrCode.MaxMerchantCityLength));
        }

        f = Tlv.Find(fields, "61");
        if (f is not null && (f.ByteLength != 8 || !AsciiText.IsDigits(f.Value)))
        {
            Warn(issues, "61", "postal code " + AsciiText.Quote(f.Value) + " should be a CEP of 8 digits without hyphen");
        }

        CheckTxId(issues, fields);
    }

    private static EmvField? Require(List<LintIssue> issues, IReadOnlyList<EmvField> fields, string id, string name)
    {
        EmvField? f = Tlv.Find(fields, id);
        if (f is null)
        {
            Error(issues, id, "missing required field (" + name + ")");
        }

        return f;
    }

    private static void CheckAccount(List<LintIssue> issues, IReadOnlyList<EmvField> fields)
    {
        EmvField? account = null;
        foreach (EmvField f in fields)
        {
            if (string.CompareOrdinal(f.Id, "26") >= 0 && string.CompareOrdinal(f.Id, "51") <= 0)
            {
                string gui = f.Child("00")?.Value ?? string.Empty;
                if (AsciiText.EqualsIgnoreCase(gui, BrCode.Gui))
                {
                    account = f;
                    if (gui != BrCode.Gui)
                    {
                        Warn(issues, f.Id + ".00", "GUI " + AsciiText.Quote(gui) + " should be lowercase " + AsciiText.Quote(BrCode.Gui));
                    }

                    break;
                }
            }
        }

        if (account is null)
        {
            Error(issues, "26", "no merchant account template (26–51) with GUI \"br.gov.bcb.pix\"");
            return;
        }

        EmvField? key = account.Child("01");
        EmvField? url = account.Child("25");
        if (key is not null && url is not null)
        {
            Error(issues, account.Id, "has both a key (01) and a URL (25); a BR Code is either static or dynamic");
        }
        else if (key is null && url is null)
        {
            Error(issues, account.Id, "has neither a key (01) nor a URL (25)");
        }

        if (key is not null)
        {
            PixException? keyError = PixKey.NormalizeCore(key.Value, out PixKey normalized);
            if (keyError is not null)
            {
                Error(issues, account.Id + ".01", keyError.Message);
            }
            else if (normalized.Value != key.Value)
            {
                Warn(issues, account.Id + ".01", "key is not in canonical form; use " + AsciiText.Quote(normalized.Value));
            }
        }

        if (url is not null && url.Value.Contains("://", StringComparison.Ordinal))
        {
            Error(issues, account.Id + ".25", "URL must not include the scheme (remove \"https://\")");
        }
    }

    private static void CheckAmount(List<LintIssue> issues, string v)
    {
        string? amountError = PixText.ParseAmount(v, out long cents);
        if (amountError is not null)
        {
            Error(issues, "54", "amount " + AsciiText.Quote(v) + ": " + amountError);
            return;
        }

        if (v.Length > 1 && v[0] == '0' && v[1] != '.')
        {
            Warn(issues, "54", "amount " + AsciiText.Quote(v) + " has leading zeros; use " + AsciiText.Quote(PixText.FormatAmount(cents)));
        }

        if (cents == 0)
        {
            Warn(issues, "54", "amount is zero; omit field 54 to let the payer choose the amount");
        }
    }

    private static void CheckTxId(List<LintIssue> issues, IReadOnlyList<EmvField> fields)
    {
        EmvField? additional = Tlv.Find(fields, "62");
        if (additional is null)
        {
            Warn(issues, "62", "missing additional data template; many PSPs expect 62 with subfield 05 (txid, \"***\" for none)");
            return;
        }

        EmvField? t = additional.Child("05");
        if (t is null)
        {
            Warn(issues, "62.05", "missing txid; use \"***\" for none");
            return;
        }

        if (t.Value == BrCode.NoTxId)
        {
            return;
        }

        string? txidError = PixText.ValidateTxId(t.Value);
        if (txidError is not null)
        {
            const string prefix = "pix: ";
            Error(issues, "62.05", txidError.StartsWith(prefix, StringComparison.Ordinal) ? txidError.Substring(prefix.Length) : txidError);
        }
    }
}
