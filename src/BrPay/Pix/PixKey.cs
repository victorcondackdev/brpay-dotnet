using System.Text;
using BrPay.Documents;
using BrPay.Internal;

namespace BrPay.Pix;

/// <summary>The type of a Pix key ("chave Pix") as registered in the DICT.</summary>
public enum PixKeyType
{
    /// <summary>Not a valid key.</summary>
    Unknown = 0,

    /// <summary>11-digit CPF.</summary>
    Cpf = 1,

    /// <summary>14-character CNPJ, numeric or alphanumeric.</summary>
    Cnpj = 2,

    /// <summary>E-mail address, lowercase, at most 77 characters.</summary>
    Email = 3,

    /// <summary>Brazilian mobile number in E.164 format, e.g. <c>+5521987654321</c>.</summary>
    Phone = 4,

    /// <summary>Random key ("chave aleatória"): lowercase UUID with hyphens.</summary>
    Evp = 5,
}

/// <summary>Extension methods for <see cref="PixKeyType"/>.</summary>
public static class PixKeyTypeExtensions
{
    /// <summary>Returns the DICT name of the key type: CPF, CNPJ, EMAIL, PHONE, EVP or UNKNOWN.</summary>
    /// <param name="type">The key type.</param>
    /// <returns>The DICT name.</returns>
    public static string ToDictName(this PixKeyType type) => type switch
    {
        PixKeyType.Cpf => "CPF",
        PixKeyType.Cnpj => "CNPJ",
        PixKeyType.Email => "EMAIL",
        PixKeyType.Phone => "PHONE",
        PixKeyType.Evp => "EVP",
        _ => "UNKNOWN",
    };
}

/// <summary>A validated Pix key in canonical form.</summary>
/// <remarks>
/// <para>The type is decided by shape, in this order:</para>
/// <list type="bullet">
/// <item><description>contains <c>@</c>: e-mail, lowercased, at most 77 characters, ASCII only, with a dotted domain;</description></item>
/// <item><description>starts with <c>+</c>: phone. Spaces, hyphens, dots and parentheses are removed. The number must be +55, a 2-digit area code (DDD) without zeros and 8 digits, or 9 digits starting with 9. National numbers without +55 are rejected, as an 11-digit string is indistinguishable from a CPF;</description></item>
/// <item><description>UUID shape (36 characters, hyphens at positions 8, 13, 18, 23): EVP, lowercased. The UUID version is not checked;</description></item>
/// <item><description>otherwise, after removing <c>.</c>, <c>-</c>, <c>/</c> and spaces: 11 characters is a CPF and 14 a CNPJ (alphanumeric allowed, uppercased), both with valid check digits.</description></item>
/// </list>
/// <para>Surrounding white space is ignored.</para>
/// </remarks>
public readonly record struct PixKey
{
    /// <summary>The maximum length of an e-mail key.</summary>
    public const int MaxEmailLength = 77;

    private const string EmailLocalSpecials = ".!#$%&'*+/=?^_`{|}~-";

    private PixKey(string value, PixKeyType type)
    {
        Value = value;
        Type = type;
    }

    /// <summary>The key in the canonical form used by the DICT and inside BR Codes.</summary>
    public string Value { get; }

    /// <summary>The key type.</summary>
    public PixKeyType Type { get; }

    /// <summary>Validates a key and returns it in canonical form.</summary>
    /// <param name="input">The key as typed by a user.</param>
    /// <returns>The normalized key.</returns>
    /// <exception cref="PixException">The key is invalid (<see cref="PixError.InvalidKey"/>).</exception>
    public static PixKey Normalize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        PixException? error = NormalizeCore(input, out PixKey key);
        return error is null ? key : throw error;
    }

    /// <summary>Validates a key without throwing.</summary>
    /// <param name="input">The key as typed by a user.</param>
    /// <param name="key">The normalized key, when valid.</param>
    /// <returns><see langword="true"/> when the key is valid.</returns>
    public static bool TryNormalize(string? input, out PixKey key)
    {
        if (input is null)
        {
            key = default;
            return false;
        }

        return NormalizeCore(input, out key) is null;
    }

    /// <summary>Returns the type of a key, or <see cref="PixKeyType.Unknown"/> when it is invalid.</summary>
    /// <param name="input">The key as typed by a user.</param>
    /// <returns>The key type.</returns>
    public static PixKeyType Detect(string? input) => TryNormalize(input, out PixKey key) ? key.Type : PixKeyType.Unknown;

    /// <summary>Returns the canonical key.</summary>
    /// <returns><see cref="Value"/>.</returns>
    public override string ToString() => Value ?? string.Empty;

    internal static PixException? NormalizeCore(string input, out PixKey key)
    {
        key = default;
        string s = AsciiText.TrimSpace(input);
        if (s.Length == 0)
        {
            return Invalid("empty");
        }

        string? value;
        PixException? error;
        PixKeyType type;
        if (s.Contains('@', StringComparison.Ordinal))
        {
            error = NormalizeEmail(s, out value);
            type = PixKeyType.Email;
        }
        else if (s[0] == '+')
        {
            error = NormalizePhone(s, out value);
            type = PixKeyType.Phone;
        }
        else if (IsUuidShape(Encoding.UTF8.GetBytes(s)))
        {
            error = NormalizeEvp(s, out value);
            type = PixKeyType.Evp;
        }
        else
        {
            string c = Document.Clean(s);
            switch (Encoding.UTF8.GetByteCount(c))
            {
                case 11:
                    DocumentError cpf = Cpf.Check(c);
                    if (cpf != DocumentError.None)
                    {
                        return Invalid("CPF: " + DocumentException.MessageFor(cpf) + " (phone keys must start with +55)");
                    }

                    key = new PixKey(c, PixKeyType.Cpf);
                    return null;
                case 14:
                    DocumentError cnpj = Cnpj.Check(c);
                    if (cnpj != DocumentError.None)
                    {
                        return Invalid("CNPJ: " + DocumentException.MessageFor(cnpj));
                    }

                    key = new PixKey(c, PixKeyType.Cnpj);
                    return null;
                default:
                    return Invalid("not a CPF, CNPJ, e-mail, phone (+55...) or EVP");
            }
        }

        if (error is not null || value is null)
        {
            return error ?? Invalid("invalid");
        }

        key = new PixKey(value, type);
        return null;
    }

    private static PixException Invalid(string detail) => PixException.Of(PixError.InvalidKey, detail);

    private static bool IsAlnumLower(byte c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

    // E-mail rules are applied to the UTF-8 bytes, exactly like the Go implementation.
    private static PixException? NormalizeEmail(string s, out string? value)
    {
        value = null;
        byte[] e = Encoding.UTF8.GetBytes(s);
        if (e.Length > MaxEmailLength)
        {
            return Invalid("e-mail longer than " + AsciiText.Str(MaxEmailLength) + " characters");
        }

        for (int i = 0; i < e.Length; i++)
        {
            if (e[i] >= 'A' && e[i] <= 'Z')
            {
                e[i] = (byte)(e[i] + ('a' - 'A'));
            }
        }

        int at = Array.IndexOf(e, (byte)'@');
        if (at != Array.LastIndexOf(e, (byte)'@'))
        {
            return Invalid("e-mail has more than one @");
        }

        ReadOnlySpan<byte> local = e.AsSpan(0, at);
        ReadOnlySpan<byte> domain = e.AsSpan(at + 1);
        if (local.Length == 0 || local.Length > 64)
        {
            return Invalid("e-mail local part must have 1 to 64 characters");
        }

        if (local[0] == '.' || local[local.Length - 1] == '.' || local.IndexOf(".."u8) >= 0)
        {
            return Invalid("misplaced dot in e-mail local part");
        }

        foreach (byte c in local)
        {
            if (!IsAlnumLower(c) && (c >= 0x80 || EmailLocalSpecials.IndexOf((char)c, StringComparison.Ordinal) < 0))
            {
                return Invalid("invalid character " + AsciiText.QuoteRune((char)c) + " in e-mail");
            }
        }

        if (domain.IndexOf((byte)'.') < 0)
        {
            return Invalid("e-mail domain must contain a dot");
        }

        while (true)
        {
            int dot = domain.IndexOf((byte)'.');
            ReadOnlySpan<byte> label = dot < 0 ? domain : domain.Slice(0, dot);
            if (label.Length == 0 || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-')
            {
                return Invalid("invalid e-mail domain");
            }

            foreach (byte c in label)
            {
                if (!IsAlnumLower(c) && c != '-')
                {
                    return Invalid("invalid character " + AsciiText.QuoteRune((char)c) + " in e-mail domain");
                }
            }

            if (dot < 0)
            {
                break;
            }

            domain = domain.Slice(dot + 1);
        }

        value = Encoding.UTF8.GetString(e);
        return null;
    }

    private static PixException? NormalizePhone(string s, out string? value)
    {
        value = null;
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        Span<char> d = stackalloc char[16];
        int n = 0;
        for (int i = 1; i < bytes.Length; i++)
        {
            byte c = bytes[i];
            if (c >= '0' && c <= '9')
            {
                if (n == d.Length)
                {
                    return Invalid("phone number too long");
                }

                d[n++] = (char)c;
            }
            else if (c != ' ' && c != '-' && c != '.' && c != '(' && c != ')')
            {
                return Invalid("invalid character " + AsciiText.QuoteRune((char)c) + " in phone number");
            }
        }

        if (n < 2 || d[0] != '5' || d[1] != '5')
        {
            return Invalid("phone keys must be Brazilian numbers (+55)");
        }

        ReadOnlySpan<char> nat = d.Slice(2, n - 2);
        if (nat.Length != 10 && nat.Length != 11)
        {
            return Invalid("phone number must have a 2-digit area code and 8 or 9 digits");
        }

        if (nat[0] == '0' || nat[1] == '0')
        {
            return Invalid("invalid area code (DDD) " + nat.Slice(0, 2).ToString());
        }

        if (nat.Length == 11 && nat[2] != '9')
        {
            return Invalid("9-digit phone numbers must start with 9");
        }

        value = "+" + d.Slice(0, n).ToString();
        return null;
    }

    private static bool IsUuidShape(byte[] s) =>
        s.Length == 36 && s[8] == '-' && s[13] == '-' && s[18] == '-' && s[23] == '-';

    private static PixException? NormalizeEvp(string s, out string? value)
    {
        value = null;
        byte[] b = Encoding.UTF8.GetBytes(s);
        for (int i = 0; i < b.Length; i++)
        {
            if (i == 8 || i == 13 || i == 18 || i == 23)
            {
                continue;
            }

            byte c = b[i];
            if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))
            {
                continue;
            }

            if (c >= 'A' && c <= 'F')
            {
                b[i] = (byte)(c + ('a' - 'A'));
                continue;
            }

            return Invalid("EVP must be a hexadecimal UUID");
        }

        value = Encoding.UTF8.GetString(b);
        return null;
    }
}
