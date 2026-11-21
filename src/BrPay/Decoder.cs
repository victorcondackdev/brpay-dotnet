using System.Text;
using System.Text.Json.Serialization;
using BrPay.Boleto;
using BrPay.Documents;
using BrPay.Internal;
using BrPay.Pix;

namespace BrPay;

/// <summary>The type of input recognized by <see cref="Decoder.Detect"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InputKind>))]
public enum InputKind
{
    /// <summary>Not recognized.</summary>
    Unknown = 0,

    /// <summary>Pix BR Code ("copia e cola").</summary>
    Pix = 1,

    /// <summary>Boleto barcode or linha digitável.</summary>
    Boleto = 2,

    /// <summary>CPF or CNPJ.</summary>
    Document = 3,

    /// <summary>Pix key that is not a CPF or CNPJ: e-mail, phone or EVP (random key).</summary>
    PixKey = 4,

    /// <summary>Pix EndToEndId.</summary>
    EndToEndId = 5,
}

/// <summary>Extension methods for <see cref="InputKind"/>.</summary>
public static class InputKindExtensions
{
    /// <summary>Returns a stable lowercase name: pix, boleto, document, pix_key, end_to_end_id or unknown (identical to the Go implementation).</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name.</returns>
    public static string ToName(this InputKind kind) => kind switch
    {
        InputKind.Pix => "pix",
        InputKind.Boleto => "boleto",
        InputKind.Document => "document",
        InputKind.PixKey => "pix_key",
        InputKind.EndToEndId => "end_to_end_id",
        _ => "unknown",
    };
}

/// <summary>Thrown (or reported in <see cref="DecodeResult.Error"/>) when input does not look like anything this library understands.</summary>
public sealed class UnrecognizedInputException : BrPayException
{
    /// <summary>Initializes the exception.</summary>
    public UnrecognizedInputException()
        : base("brpay: unrecognized input")
    {
    }
}

/// <summary>A validated CPF or CNPJ.</summary>
public sealed record DecodedDocument
{
    /// <summary>CPF or CNPJ.</summary>
    public DocumentKind Kind { get; init; }

    /// <summary>The bare identifier: 11 digits, or 14 characters for a CNPJ (uppercase letters allowed in the first 12).</summary>
    public string Number { get; init; } = string.Empty;

    /// <summary>The punctuated form, e.g. <c>529.982.247-25</c>.</summary>
    public string Formatted { get; init; } = string.Empty;

    /// <summary>The number with its middle hidden for logs, e.g. <c>***.982.247-**</c>.</summary>
    public string Masked { get; init; } = string.Empty;

    /// <summary><see langword="true"/> for a CNPJ in the alphanumeric format.</summary>
    public bool Alphanumeric { get; init; }
}

/// <summary>
/// The outcome of <see cref="Decoder.Decode"/>. <see cref="Kind"/> says
/// which one of <see cref="Pix"/>, <see cref="Boleto"/>, <see cref="Document"/>,
/// <see cref="PixKey"/> and <see cref="EndToEndId"/> is set; on error none is
/// set, but <see cref="Kind"/>, <see cref="Issues"/> and <see cref="Suggestions"/> are still filled in.
/// </summary>
public sealed record DecodeResult
{
    /// <summary>The detected kind of input.</summary>
    public InputKind Kind { get; init; }

    /// <summary>The decoded BR Code, for <see cref="InputKind.Pix"/>.</summary>
    public BrCode? Pix { get; init; }

    /// <summary>The parsed boleto, for <see cref="InputKind.Boleto"/>.</summary>
    public BoletoCode? Boleto { get; init; }

    /// <summary>The validated document, for <see cref="InputKind.Document"/>.</summary>
    public DecodedDocument? Document { get; init; }

    /// <summary>The normalized key, for <see cref="InputKind.PixKey"/>.</summary>
    public PixKey? PixKey { get; init; }

    /// <summary>The parsed identifier, for <see cref="InputKind.EndToEndId"/>.</summary>
    public EndToEndId? EndToEndId { get; init; }

    /// <summary>
    /// The findings of <see cref="Linter.Lint"/> for <see cref="InputKind.Pix"/>,
    /// computed even when the payload does not decode. A payload can decode and still have issues.
    /// </summary>
    public IReadOnlyList<LintIssue> Issues { get; init; } = Array.Empty<LintIssue>();

    /// <summary>Corrected linhas digitáveis from <see cref="BoletoCode.SuggestFix"/> when a linha digitável fails a check digit.</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Why decoding failed, or <see langword="null"/> on success. The
    /// exception comes unchanged from the underlying namespace, so it can be
    /// matched by type (e.g. <see cref="CheckDigitException"/> or <see cref="Emv.EmvParseException"/>).
    /// Not serialized; see <see cref="ErrorMessage"/>.
    /// </summary>
    [JsonIgnore]
    public BrPayException? Error { get; init; }

    /// <summary>The message of <see cref="Error"/>, or <see langword="null"/> on success.</summary>
    public string? ErrorMessage => Error?.Message;

    /// <summary><see langword="true"/> when the input was recognized and decoded.</summary>
    public bool Success => Error is null;
}

/// <summary>
/// The single entry point for whatever a user pasted into a payment field: a
/// Pix "copia e cola" payload, a boleto barcode or linha digitável, a CPF or
/// CNPJ, a Pix key or an EndToEndId.
/// </summary>
public static class Decoder
{
    /// <summary>
    /// Classifies input by its shape, without fully validating it. The rules,
    /// applied to the input with surrounding white space removed, in order:
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><description>Digits with optional spaces, dots and hyphens, 44, 47 or 48 of them, that form a valid boleto: <see cref="InputKind.Boleto"/>. A valid boleto wins over everything else, including the "000201" Pix prefix.</description></item>
    /// <item><description>Starts with "000201" (the BR Code payload format indicator): <see cref="InputKind.Pix"/>.</description></item>
    /// <item><description>Same shape as rule 1 but with a check-digit or layout error: <see cref="InputKind.Boleto"/>, so that decoding reports the boleto error and suggestions.</description></item>
    /// <item><description>11 digits, or 14 characters in CNPJ shape (numeric or alphanumeric), after removing ".", "-", "/" and spaces: <see cref="InputKind.Document"/>.</description></item>
    /// <item><description>32 characters starting with "E": <see cref="InputKind.EndToEndId"/>.</description></item>
    /// <item><description>Contains "@", starts with "+", or has the UUID shape: <see cref="InputKind.PixKey"/>.</description></item>
    /// </list>
    /// <para>Anything else is <see cref="InputKind.Unknown"/>. The rules are identical to the Go implementation.</para>
    /// </remarks>
    /// <param name="input">The input.</param>
    /// <returns>The detected kind.</returns>
    public static InputKind Detect(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string s = AsciiText.TrimSpace(input);
        if (s.Length == 0)
        {
            return InputKind.Unknown;
        }

        bool boletoShape = IsBoletoShape(s);
        if (boletoShape && BoletoCode.IsValid(s))
        {
            return InputKind.Boleto;
        }

        if (s.StartsWith("000201", StringComparison.Ordinal))
        {
            return InputKind.Pix;
        }

        if (boletoShape)
        {
            return InputKind.Boleto;
        }

        if (IsDocumentShape(s))
        {
            return InputKind.Document;
        }

        if (s[0] == 'E' && Encoding.UTF8.GetByteCount(s) == BrPay.Pix.EndToEndId.Length)
        {
            return InputKind.EndToEndId;
        }

        if (s.Contains('@', StringComparison.Ordinal) || s[0] == '+' || IsUuidShape(s))
        {
            return InputKind.PixKey;
        }

        return InputKind.Unknown;
    }

    /// <summary>
    /// Detects the kind of input (see <see cref="Detect"/>) and decodes it
    /// with the matching namespace. It never throws for any non-null input:
    /// failures are reported in <see cref="DecodeResult.Error"/>.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns>The result.</returns>
    public static DecodeResult Decode(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        InputKind kind = Detect(input);
        string s = AsciiText.TrimSpace(input);
        switch (kind)
        {
            case InputKind.Pix:
            {
                IReadOnlyList<LintIssue> issues = Linter.Lint(input);
                BrCode? code = BrCode.DecodeCore(s, out BrPayException? error);
                return new DecodeResult { Kind = kind, Pix = code, Issues = issues, Error = error };
            }

            case InputKind.Boleto:
            {
                BoletoCode? boleto = BoletoCode.ParseCore(s, out BoletoException? error);
                IReadOnlyList<string> suggestions = error is CheckDigitException ? BoletoCode.SuggestFix(s) : Array.Empty<string>();
                return new DecodeResult { Kind = kind, Boleto = boleto, Suggestions = suggestions, Error = error };
            }

            case InputKind.Document:
                return DecodeDocument(s);

            case InputKind.EndToEndId:
            {
                PixException? error = BrPay.Pix.EndToEndId.ParseCore(s, out EndToEndId id);
                return error is null
                    ? new DecodeResult { Kind = kind, EndToEndId = id }
                    : new DecodeResult { Kind = kind, Error = error };
            }

            case InputKind.PixKey:
            {
                PixException? error = BrPay.Pix.PixKey.NormalizeCore(s, out PixKey key);
                return error is null
                    ? new DecodeResult { Kind = kind, PixKey = key }
                    : new DecodeResult { Kind = kind, Error = error };
            }

            default:
                return new DecodeResult { Kind = InputKind.Unknown, Error = new UnrecognizedInputException() };
        }
    }

    private static DecodeResult DecodeDocument(string s)
    {
        string n = Document.Clean(s);
        bool cpf = n.Length == Cpf.Length;
        DocumentError error = cpf ? Cpf.Check(n) : Cnpj.Check(n);
        DocumentKind kind = cpf ? DocumentKind.Cpf : DocumentKind.Cnpj;
        if (error != DocumentError.None)
        {
            return new DecodeResult { Kind = InputKind.Document, Error = new DocumentException(kind, error) };
        }

        return new DecodeResult
        {
            Kind = InputKind.Document,
            Document = new DecodedDocument
            {
                Kind = kind,
                Number = n,
                Formatted = Document.Format(n),
                Masked = Document.Mask(n),
                Alphanumeric = Cnpj.IsAlphanumeric(n),
            },
        };
    }

    // 44, 47 or 48 digits once the separators boleto parsing ignores are removed.
    private static bool IsBoletoShape(string s)
    {
        int n = 0;
        foreach (char c in s)
        {
            if (c >= '0' && c <= '9')
            {
                n++;
            }
            else if (c != ' ' && c != '\t' && c != '\n' && c != '\r' && c != '.' && c != '-')
            {
                return false;
            }
        }

        return n == BoletoCode.BarcodeLength || n == BoletoCode.BancarioLineLength || n == BoletoCode.ArrecadacaoLineLength;
    }

    // 11 digits (CPF) or 12 alphanumeric characters followed by 2 digits
    // (CNPJ) once the usual punctuation is removed; lowercase is accepted.
    private static bool IsDocumentShape(string s)
    {
        foreach (char ch in s)
        {
            if (!AsciiText.IsAlnum(ch) && ch != '.' && ch != '-' && ch != '/' && ch != ' ')
            {
                return false;
            }
        }

        string c = Document.Clean(s);
        switch (c.Length)
        {
            case 11:
                return AsciiText.IsDigits(c);
            case 14:
                for (int i = 0; i < 12; i++)
                {
                    if (!AsciiText.IsAlnum(c[i]))
                    {
                        return false;
                    }
                }

                return AsciiText.IsDigits(c.AsSpan(12));
            default:
                return false;
        }
    }

    private static bool IsUuidShape(string s)
    {
        if (s.Length != 36)
        {
            return false;
        }

        for (int i = 0; i < s.Length; i++)
        {
            if (i == 8 || i == 13 || i == 18 || i == 23)
            {
                if (s[i] != '-')
                {
                    return false;
                }
            }
            else if (!AsciiText.IsAlnum(s[i]))
            {
                return false;
            }
        }

        return true;
    }
}
