using System.Diagnostics.CodeAnalysis;
using BrPay.Emv;
using BrPay.Internal;

namespace BrPay.Pix;

/// <summary>A decoded Pix BR Code ("copia e cola" payload).</summary>
/// <remarks>
/// <see cref="Decode"/> is deliberately lenient about content it does not
/// need to interpret (an unrecognized key format, unknown templates) and
/// preserves every field in <see cref="Fields"/>, so codes for products the
/// BCB introduces later are not lost. Use <see cref="Linter.Lint"/> for strict checking.
/// </remarks>
public sealed record BrCode
{
    /// <summary>The globally unique identifier of the Pix merchant account template. Matched case-insensitively.</summary>
    public const string Gui = "br.gov.bcb.pix";

    /// <summary>The maximum length of a BR Code payload, in bytes.</summary>
    public const int MaxPayloadLength = 512;

    /// <summary>Field 01 value of a static or reusable code.</summary>
    public const string InitiationReusable = "11";

    /// <summary>Field 01 value of a single-use code.</summary>
    public const string InitiationSingleUse = "12";

    /// <summary>The largest amount, in cents, that fits in field 54 (<c>9999999999.99</c>).</summary>
    public const long MaxCents = 999_999_999_999;

    /// <summary>The maximum merchant name length enforced by <see cref="BrCodeBuilder"/>.</summary>
    public const int MaxMerchantNameLength = 25;

    /// <summary>The maximum merchant city length enforced by <see cref="BrCodeBuilder"/>.</summary>
    public const int MaxMerchantCityLength = 15;

    /// <summary>The maximum transaction identifier length.</summary>
    public const int MaxTxIdLength = 25;

    /// <summary>The field 62.05 value meaning "no transaction identifier".</summary>
    internal const string NoTxId = "***";

    /// <summary>The raw value of field 01 (<c>"11"</c> or <c>"12"</c>), or <see langword="null"/> when absent.</summary>
    public string? PointOfInitiation { get; init; }

    /// <summary><see langword="false"/> only when <see cref="PointOfInitiation"/> is <c>"12"</c> (single use).</summary>
    public bool IsReusable { get; init; }

    /// <summary><see langword="true"/> when the code carries a Pix key: all payment data is in the payload.</summary>
    public bool IsStatic { get; init; }

    /// <summary><see langword="true"/> when the code carries a URL: the payment data is fetched from the PSP. Exactly one of <see cref="IsStatic"/> and <see cref="IsDynamic"/> is set.</summary>
    public bool IsDynamic { get; init; }

    /// <summary>The ID (26–51) of the template whose GUI is <c>br.gov.bcb.pix</c>.</summary>
    public string MerchantAccountId { get; init; } = string.Empty;

    /// <summary>The Pix key exactly as written in the payload (static codes), or <see langword="null"/>.</summary>
    public string? Key { get; init; }

    /// <summary>The detected type of <see cref="Key"/>, or <see cref="PixKeyType.Unknown"/> when absent or invalid.</summary>
    public PixKeyType KeyType { get; init; }

    /// <summary>The free-text additional information (subfield 02), or <see langword="null"/>.</summary>
    public string? Description { get; init; }

    /// <summary>The payload location without scheme (dynamic codes), e.g. <c>pix.example.com/qr/v2/9d36b84f</c>; fetched over HTTPS.</summary>
    public string? Url { get; init; }

    /// <summary>The merchant category code (field 52), <c>"0000"</c> if unspecified.</summary>
    public string Mcc { get; init; } = string.Empty;

    /// <summary>The ISO 4217 numeric currency code (field 53), <c>"986"</c> for BRL.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>The raw transaction amount (field 54), or <see langword="null"/> when the payer chooses the amount.</summary>
    public string? RawAmount { get; init; }

    /// <summary>The amount in cents; 0 when <see cref="RawAmount"/> is absent.</summary>
    public long Cents { get; init; }

    /// <summary>The amount in reais, for display. Transport and arithmetic should use <see cref="Cents"/>.</summary>
    public decimal Amount => Cents / 100m;

    /// <summary>The ISO 3166-1 alpha-2 country code (field 58).</summary>
    public string CountryCode { get; init; } = string.Empty;

    /// <summary>The receiver name (field 59).</summary>
    public string MerchantName { get; init; } = string.Empty;

    /// <summary>The receiver city (field 60).</summary>
    public string MerchantCity { get; init; } = string.Empty;

    /// <summary>The receiver postal code (field 61), or <see langword="null"/>.</summary>
    public string? PostalCode { get; init; }

    /// <summary>The transaction identifier (field 62, subfield 05); <see langword="null"/> when absent or <c>"***"</c>.</summary>
    public string? TxId { get; init; }

    /// <summary>The checksum (field 63) as written.</summary>
    public string Crc { get; init; } = string.Empty;

    /// <summary>
    /// Maps the ID of every merchant account (26–51) and unreserved (80–99)
    /// template to its GUI (subfield 00, empty if missing), so templates of
    /// other schemes or future BCB products are visible.
    /// </summary>
    public IReadOnlyDictionary<string, string> Templates { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Every parsed field in payload order, including those this library does not interpret.</summary>
    public IReadOnlyList<EmvField> Fields { get; init; } = Array.Empty<EmvField>();

    /// <summary>Returns the top-level field with the given ID, with its subfields in <see cref="EmvField.Children"/>.</summary>
    /// <param name="id">The two-digit field ID.</param>
    /// <returns>The field, or <see langword="null"/>.</returns>
    public EmvField? Template(string id) => Tlv.Find(Fields, id);

    /// <summary>
    /// Parses and validates a Pix payload. Surrounding white space is
    /// ignored. It verifies TLV syntax, structure and CRC, requires a
    /// <c>br.gov.bcb.pix</c> merchant account with either a key or a URL, the
    /// fields 52, 53, 58, 59 and 60, and a well-formed amount if field 54 is present.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <returns>The decoded code.</returns>
    /// <exception cref="EmvParseException">The TLV syntax or payload structure is invalid.</exception>
    /// <exception cref="EmvCrcException">The checksum does not match.</exception>
    /// <exception cref="PixException">The payload is not a valid Pix BR Code.</exception>
    public static BrCode Decode(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return DecodeCore(payload, out BrPayException? error) ?? throw error!;
    }

    /// <summary>Decodes a payload like <see cref="Decode"/> without throwing.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="code">The decoded code, when successful.</param>
    /// <returns><see langword="true"/> when the payload is a valid BR Code.</returns>
    public static bool TryDecode(string? payload, [NotNullWhen(true)] out BrCode? code)
    {
        if (payload is null)
        {
            code = null;
            return false;
        }

        code = DecodeCore(payload, out _);
        return code is not null;
    }

    /// <summary>Decodes a payload, reporting the failure instead of throwing it.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="code">The decoded code, when successful.</param>
    /// <param name="error">The reason decoding failed, when unsuccessful: an <see cref="EmvParseException"/>, <see cref="EmvCrcException"/> or <see cref="PixException"/>.</param>
    /// <returns><see langword="true"/> when the payload is a valid BR Code.</returns>
    public static bool TryDecode(string payload, [NotNullWhen(true)] out BrCode? code, [NotNullWhen(false)] out BrPayException? error)
    {
        ArgumentNullException.ThrowIfNull(payload);
        code = DecodeCore(payload, out error);
        if (code is not null)
        {
            return true;
        }

        error ??= PixException.Of(PixError.NotPix);
        return false;
    }

    private static ReadOnlySpan<byte> RequiredFieldIds => new byte[] { 52, 53, 58, 59, 60 };

    private static string RequiredFieldName(int id) => id switch
    {
        52 => "merchant category code",
        53 => "transaction currency",
        58 => "country code",
        59 => "merchant name",
        _ => "merchant city",
    };

    internal static BrCode? DecodeCore(string payload, out BrPayException? error)
    {
        string p = AsciiText.TrimSpace(payload);
        IReadOnlyList<EmvField>? fields = Tlv.ParseCore(p, expand: true, out EmvParseException? parseError);
        if (fields is null)
        {
            error = parseError;
            return null;
        }

        error = (BrPayException?)Tlv.ValidateCore(fields) ?? Crc16.VerifyCore(p);
        if (error is not null)
        {
            return null;
        }

        var templates = new Dictionary<string, string>(StringComparer.Ordinal);
        Span<bool> seen = stackalloc bool[100];
        string? poi = null, accountId = null, key = null, description = null, url = null, rawAmount = null;
        string? postal = null, txid = null;
        string mcc = string.Empty, currency = string.Empty, country = string.Empty;
        string name = string.Empty, city = string.Empty, crc = string.Empty;
        long cents = 0;

        foreach (EmvField f in fields)
        {
            _ = Tlv.TryId(f.Id, out int n); // Validate checked the ID.
            seen[n] = true;
            if ((n >= 26 && n <= 51) || n >= 80)
            {
                string gui = f.Child("00")?.Value ?? string.Empty;
                templates[f.Id] = gui;
                if (n <= 51 && accountId is null && AsciiText.EqualsIgnoreCase(gui, Gui))
                {
                    accountId = f.Id;
                    foreach (EmvField s in f.Children)
                    {
                        switch (s.Id)
                        {
                            case "01": key = s.Value; break;
                            case "02": description = s.Value; break;
                            case "25": url = s.Value; break;
                        }
                    }
                }

                continue;
            }

            switch (n)
            {
                case 1: poi = f.Value; break;
                case 52: mcc = f.Value; break;
                case 53: currency = f.Value; break;
                case 54:
                    string? amountError = PixText.ParseAmount(f.Value, out cents);
                    if (amountError is not null)
                    {
                        error = new PixException(PixError.InvalidAmount, PixException.MessageFor(PixError.InvalidAmount) + " " + AsciiText.Quote(f.Value) + ": " + amountError);
                        return null;
                    }

                    rawAmount = f.Value;
                    break;
                case 58: country = f.Value; break;
                case 59: name = f.Value; break;
                case 60: city = f.Value; break;
                case 61: postal = f.Value; break;
                case 62:
                    EmvField? t = f.Child("05");
                    if (t is not null && t.Value != NoTxId)
                    {
                        txid = t.Value;
                    }

                    break;
                case 63: crc = f.Value; break;
            }
        }

        if (accountId is null)
        {
            error = PixException.Of(PixError.NotPix);
            return null;
        }

        if (key is not null && url is not null)
        {
            error = PixException.Of(PixError.KeyAndUrl);
            return null;
        }

        if (key is null && url is null)
        {
            error = PixException.Of(PixError.NoKeyOrUrl);
            return null;
        }

        foreach (byte id in RequiredFieldIds)
        {
            if (!seen[id])
            {
                error = PixException.Of(PixError.MissingField, AsciiText.Str(id) + " (" + RequiredFieldName(id) + ")");
                return null;
            }
        }

        return new BrCode
        {
            PointOfInitiation = poi,
            IsReusable = poi != InitiationSingleUse,
            IsStatic = key is not null,
            IsDynamic = url is not null,
            MerchantAccountId = accountId,
            Key = key,
            KeyType = key is null ? PixKeyType.Unknown : PixKey.Detect(key),
            Description = description,
            Url = url,
            Mcc = mcc,
            Currency = currency,
            RawAmount = rawAmount,
            Cents = rawAmount is null ? 0 : cents,
            CountryCode = country,
            MerchantName = name,
            MerchantCity = city,
            PostalCode = postal,
            TxId = txid,
            Crc = crc,
            Templates = templates,
            Fields = fields,
        };
    }
}
