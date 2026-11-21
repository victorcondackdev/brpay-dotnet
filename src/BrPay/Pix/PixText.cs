using System.Text;
using BrPay.Internal;

namespace BrPay.Pix;

/// <summary>Text, amount and txid rules shared by the decoder, the builder and the linter.</summary>
internal static class PixText
{
    public const int MaxAmountLength = 13;

    public const string AmountSyntax = "want digits with an optional dot and up to 2 decimals, e.g. 10.50";
    public const string AmountTooLong = "longer than 13 characters";
    public const string AmountComma = "uses a comma; the decimal separator must be a dot";
    public const string AmountDecimals = "more than 2 decimal places";

    /// <summary>
    /// Trims and transliterates Portuguese accented letters to ASCII. Returns
    /// an error description for any other character outside printable ASCII.
    /// </summary>
    public static string? ToAscii(string? input, out string result)
    {
        string s = AsciiText.TrimSpace(input ?? string.Empty);
        result = s;
        if (AsciiText.IsPrintable(s))
        {
            return null;
        }

        var b = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= 0x20 && c <= 0x7E)
            {
                b.Append(c);
                continue;
            }

            char folded = Fold(c);
            if (folded != '\0')
            {
                b.Append(folded);
                continue;
            }

            int at = Encoding.UTF8.GetByteCount(s.AsSpan(0, i));
            result = string.Empty;
            if (c == (char)0xFFFD)
            {
                // Go decodes invalid UTF-8 to U+FFFD and reports it this way.
                return "invalid UTF-8 at byte " + AsciiText.Str(at);
            }

            if (char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])))
            {
                return "invalid UTF-16 at byte " + AsciiText.Str(at);
            }

            return "unsupported character " + AsciiText.QuoteRune(AsciiText.RuneAt(s, i)) + " at byte " + AsciiText.Str(at);
        }

        result = b.ToString();
        return null;
    }

    private static char Fold(char c) => c switch
    {
        'á' or 'à' or 'â' or 'ã' or 'ä' => 'a',
        'é' or 'è' or 'ê' or 'ë' => 'e',
        'í' or 'ì' or 'î' or 'ï' => 'i',
        'ó' or 'ò' or 'ô' or 'õ' or 'ö' => 'o',
        'ú' or 'ù' or 'û' or 'ü' => 'u',
        'ç' => 'c',
        'ñ' => 'n',
        'Á' or 'À' or 'Â' or 'Ã' or 'Ä' => 'A',
        'É' or 'È' or 'Ê' or 'Ë' => 'E',
        'Í' or 'Ì' or 'Î' or 'Ï' => 'I',
        'Ó' or 'Ò' or 'Ô' or 'Õ' or 'Ö' => 'O',
        'Ú' or 'Ù' or 'Û' or 'Ü' => 'U',
        'Ç' => 'C',
        'Ñ' => 'N',
        _ => '\0',
    };

    /// <summary>
    /// Converts the value of field 54 to cents. Accepts "10", "10.5" and
    /// "10.50"; leading zeros are accepted (the linter warns). Returns an
    /// error description on failure.
    /// </summary>
    public static string? ParseAmount(string s, out long cents)
    {
        cents = 0;
        int byteLength = Encoding.UTF8.GetByteCount(s);
        if (byteLength > MaxAmountLength)
        {
            return AmountTooLong;
        }

        if (s.Length == 0)
        {
            return AmountSyntax;
        }

        long whole = 0;
        long frac = 0;
        bool dot = false;
        int decimals = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= '0' && c <= '9')
            {
                if (dot)
                {
                    decimals++;
                    if (decimals > 2)
                    {
                        return AmountDecimals;
                    }

                    frac = (frac * 10) + (c - '0');
                }
                else
                {
                    whole = (whole * 10) + (c - '0');
                }
            }
            else if (c == ',')
            {
                return AmountComma;
            }
            else if (c == '.' && !dot && i > 0)
            {
                dot = true;
            }
            else
            {
                return AmountSyntax;
            }
        }

        if (dot && decimals == 0)
        {
            return AmountSyntax;
        }

        if (decimals == 1)
        {
            frac *= 10;
        }

        cents = (whole * 100) + frac;
        return null;
    }

    /// <summary>Renders cents as field 54 expects: "10.50", "0.01".</summary>
    public static string FormatAmount(long cents)
    {
        long whole = cents / 100;
        long rest = cents % 100;
        return AsciiText.Str(whole) + "." + (char)('0' + (rest / 10)) + (char)('0' + (rest % 10));
    }

    /// <summary>Validates a txid: at most 25 bytes from [A-Za-z0-9]. Returns the full error message, or null.</summary>
    public static string? ValidateTxId(string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length > BrCode.MaxTxIdLength)
        {
            return PixException.MessageFor(PixError.InvalidTxId) + ": " + AsciiText.Str(bytes.Length) + " characters, max " + AsciiText.Str(BrCode.MaxTxIdLength);
        }

        for (int i = 0; i < bytes.Length; i++)
        {
            byte c = bytes[i];
            if (!AsciiText.IsAlnum((char)c))
            {
                return PixException.MessageFor(PixError.InvalidTxId) + ": invalid character " + AsciiText.QuoteRune((char)c) + " at position " + AsciiText.Str(i) + ", want [A-Za-z0-9]";
            }
        }

        return null;
    }
}
