using System.Text;
using BrPay.Emv;
using BrPay.Internal;

namespace BrPay.Pix;

/// <summary>Describes a static BR Code, which carries a Pix key and can be paid any number of times.</summary>
public sealed record StaticBrCodeOptions
{
    /// <summary>The receiver's Pix key, in any format <see cref="PixKey.Normalize"/> accepts.</summary>
    public required string Key { get; init; }

    /// <summary>The receiver name, at most 25 characters after transliteration.</summary>
    public required string MerchantName { get; init; }

    /// <summary>The receiver city, at most 15 characters after transliteration.</summary>
    public required string MerchantCity { get; init; }

    /// <summary>The amount in cents. Zero leaves the amount open for the payer to fill in.</summary>
    public long Cents { get; init; }

    /// <summary>Identifies the payment for reconciliation: up to 25 characters from [A-Za-z0-9]. Empty or <see langword="null"/> means none (<c>"***"</c>).</summary>
    public string? TxId { get; init; }

    /// <summary>Optional free text shown to the payer. Its length is bounded by the 99-byte merchant account template it shares with the key.</summary>
    public string? Description { get; init; }

    /// <summary>The optional receiver CEP, 8 digits; a hyphen is allowed.</summary>
    public string? PostalCode { get; init; }

    /// <summary>The merchant category code, 4 digits; empty or <see langword="null"/> means <c>"0000"</c>.</summary>
    public string? Mcc { get; init; }
}

/// <summary>Describes a dynamic BR Code, whose payment data is served by the receiver's PSP.</summary>
public sealed record DynamicBrCodeOptions
{
    /// <summary>The payload location. A leading <c>https://</c> is removed, as the BR Code stores the location without scheme.</summary>
    public required string Url { get; init; }

    /// <summary>The receiver name, at most 25 characters after transliteration.</summary>
    public required string MerchantName { get; init; }

    /// <summary>The receiver city, at most 15 characters after transliteration.</summary>
    public required string MerchantCity { get; init; }

    /// <summary>The optional receiver CEP, 8 digits; a hyphen is allowed.</summary>
    public string? PostalCode { get; init; }

    /// <summary>The merchant category code, 4 digits; empty or <see langword="null"/> means <c>"0000"</c>.</summary>
    public string? Mcc { get; init; }

    /// <summary>Marks the code as reusable (field 01 = <c>"11"</c>). By default the code is single-use (<c>"12"</c>).</summary>
    public bool Reusable { get; init; }
}

/// <summary>Builds Pix BR Code payloads, CRC included.</summary>
/// <remarks>
/// Text fields (merchant name, city, description) are always trimmed and
/// Portuguese accented letters are transliterated to ASCII (<c>"São Paulo"</c>
/// becomes <c>"Sao Paulo"</c>); any other non-ASCII character is an error.
/// Limits are checked after transliteration. The output is byte-for-byte
/// identical to the Go implementation for the same options.
/// </remarks>
public static class BrCodeBuilder
{
    /// <summary>
    /// Builds a static BR Code. The key is stored in its normalized form and
    /// field 01 (point of initiation) is omitted, which means reusable.
    /// </summary>
    /// <param name="options">The code description.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="PixException">An option is invalid.</exception>
    public static string BuildStatic(StaticBrCodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        PixException? keyError = PixKey.NormalizeCore(options.Key ?? string.Empty, out PixKey key);
        if (keyError is not null)
        {
            throw keyError;
        }

        string? descError = PixText.ToAscii(options.Description, out string desc);
        if (descError is not null)
        {
            throw PixException.Of(PixError.InvalidDescription, descError);
        }

        var account = new List<EmvField> { new EmvField("00", BrCode.Gui), new EmvField("01", key.Value) };
        if (desc.Length > 0)
        {
            account.Add(new EmvField("02", desc));
        }

        int n = TemplateLength(account);
        if (n > Tlv.MaxValueLength)
        {
            throw PixException.Of(PixError.InvalidDescription, "key and description take " + AsciiText.Str(n) + " bytes in the merchant account template, max " + AsciiText.Str(Tlv.MaxValueLength));
        }

        if (options.Cents < 0 || options.Cents > BrCode.MaxCents)
        {
            throw PixException.Of(PixError.InvalidAmount, AsciiText.Str(options.Cents) + " cents is out of range");
        }

        string txid = options.TxId ?? string.Empty;
        if (txid.Length == 0)
        {
            txid = BrCode.NoTxId;
        }
        else
        {
            string? txidError = PixText.ValidateTxId(txid);
            if (txidError is not null)
            {
                throw new PixException(PixError.InvalidTxId, txidError);
            }
        }

        string? amount = options.Cents > 0 ? PixText.FormatAmount(options.Cents) : null;
        return Build(null, account, amount, txid, options.MerchantName, options.MerchantCity, options.PostalCode, options.Mcc);
    }

    /// <summary>
    /// Builds a dynamic BR Code. The amount and txid are not part of a
    /// dynamic payload; they are served by the PSP at the URL.
    /// </summary>
    /// <param name="options">The code description.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="PixException">An option is invalid.</exception>
    public static string BuildDynamic(DynamicBrCodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string u = AsciiText.TrimSpace(options.Url ?? string.Empty);
        if (u.Length >= 8 && AsciiText.EqualsIgnoreCase(u.AsSpan(0, 8), "https://"))
        {
            u = u.Substring(8);
        }

        if (u.Length == 0)
        {
            throw PixException.Of(PixError.InvalidUrl, "empty");
        }

        if (u.Contains("://", StringComparison.Ordinal))
        {
            throw PixException.Of(PixError.InvalidUrl, "only https URLs are allowed");
        }

        if (!AsciiText.IsPrintable(u) || u.Contains(' ', StringComparison.Ordinal))
        {
            throw PixException.Of(PixError.InvalidUrl, "must be printable ASCII without spaces");
        }

        var account = new List<EmvField> { new EmvField("00", BrCode.Gui), new EmvField("25", u) };
        int n = TemplateLength(account);
        if (n > Tlv.MaxValueLength)
        {
            throw PixException.Of(PixError.InvalidUrl, AsciiText.Str(n) + " bytes in the merchant account template, max " + AsciiText.Str(Tlv.MaxValueLength));
        }

        string poi = options.Reusable ? BrCode.InitiationReusable : BrCode.InitiationSingleUse;
        return Build(poi, account, null, BrCode.NoTxId, options.MerchantName, options.MerchantCity, options.PostalCode, options.Mcc);
    }

    private static string Build(string? poi, List<EmvField> account, string? amount, string txid, string? name, string? city, string? postal, string? mcc)
    {
        string merchantName = LimitedText(name, BrCode.MaxMerchantNameLength, PixError.InvalidMerchantName);
        string merchantCity = LimitedText(city, BrCode.MaxMerchantCityLength, PixError.InvalidMerchantCity);

        string postalCode = AsciiText.TrimSpace(postal ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal);
        if (postalCode.Length > 0 && (postalCode.Length != 8 || !AsciiText.IsDigits(postalCode)))
        {
            throw PixException.Of(PixError.InvalidPostalCode, "want 8 digits");
        }

        string category = mcc ?? string.Empty;
        if (category.Length == 0)
        {
            category = "0000";
        }
        else if (category.Length != 4 || !AsciiText.IsDigits(category))
        {
            throw PixException.Of(PixError.InvalidMcc, "want 4 digits");
        }

        var fields = new List<EmvField>(11) { new EmvField("00", "01") };
        if (poi is not null)
        {
            fields.Add(new EmvField("01", poi));
        }

        fields.Add(new EmvField("26", string.Empty, account));
        fields.Add(new EmvField("52", category));
        fields.Add(new EmvField("53", "986"));
        if (amount is not null)
        {
            fields.Add(new EmvField("54", amount));
        }

        fields.Add(new EmvField("58", "BR"));
        fields.Add(new EmvField("59", merchantName));
        fields.Add(new EmvField("60", merchantCity));
        if (postalCode.Length > 0)
        {
            fields.Add(new EmvField("61", postalCode));
        }

        fields.Add(EmvField.Template("62", new EmvField("05", txid)));

        string payload = Crc16.Append(Tlv.Encode(fields));
        int length = Encoding.UTF8.GetByteCount(payload);
        if (length > BrCode.MaxPayloadLength)
        {
            throw PixException.Of(PixError.PayloadTooLong, AsciiText.Str(length) + " bytes, max " + AsciiText.Str(BrCode.MaxPayloadLength));
        }

        return payload;
    }

    private static string LimitedText(string? s, int max, PixError error)
    {
        string? textError = PixText.ToAscii(s, out string t);
        if (textError is not null)
        {
            throw PixException.Of(error, textError);
        }

        if (t.Length == 0)
        {
            throw PixException.Of(error, "empty");
        }

        if (t.Length > max)
        {
            throw PixException.Of(error, AsciiText.Str(t.Length) + " characters, max " + AsciiText.Str(max));
        }

        return t;
    }

    private static int TemplateLength(List<EmvField> fields)
    {
        int n = 0;
        foreach (EmvField f in fields)
        {
            n += 4 + Encoding.UTF8.GetByteCount(f.Value);
        }

        return n;
    }
}
