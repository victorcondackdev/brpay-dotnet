using System.Buffers;
using System.Globalization;
using System.Text;

namespace BrPay.Internal;

/// <summary>Byte-level helpers that mirror the Go implementation exactly.</summary>
internal static class AsciiText
{
    public static bool IsDigit(char c) => c >= '0' && c <= '9';

    public static bool IsAlnum(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    public static bool IsDigits(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reports whether every character is printable ASCII (0x20–0x7E).</summary>
    public static bool IsPrintable(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (c < 0x20 || c > 0x7E)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>ASCII-only case-insensitive comparison (Go <c>strings.EqualFold</c> on ASCII targets).</summary>
    public static bool EqualsIgnoreCase(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (ToLower(a[i]) != ToLower(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static char ToLower(char c) => c >= 'A' && c <= 'Z' ? (char)(c + ('a' - 'A')) : c;

    /// <summary>
    /// Removes leading and trailing white space as defined by Go's
    /// <c>unicode.IsSpace</c>, so trimming is identical on both platforms.
    /// </summary>
    public static string TrimSpace(string s)
    {
        int start = 0;
        int end = s.Length;
        while (start < end && IsSpace(s[start]))
        {
            start++;
        }

        while (end > start && IsSpace(s[end - 1]))
        {
            end--;
        }

        return start == 0 && end == s.Length ? s : s.Substring(start, end - start);
    }

    /// <summary>Go's <c>unicode.IsSpace</c> for UTF-16 code units (all white space is in the BMP).</summary>
    public static bool IsSpace(char c) => (int)c switch
    {
        0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x20 or 0x85 or 0xA0 => true,
        0x1680 or 0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 => true,
        >= 0x2000 and <= 0x200A => true,
        _ => false,
    };

    /// <summary>Formats a value with Go's <c>%q</c> verb for strings (<c>strconv.Quote</c>).</summary>
    public static string Quote(string s)
    {
        var b = new StringBuilder(s.Length + 2);
        b.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' || c == '\\')
            {
                b.Append('\\').Append(c);
            }
            else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                b.Append(c).Append(s[i + 1]);
                i++;
            }
            else
            {
                AppendEscaped(b, c);
            }
        }

        return b.Append('"').ToString();
    }

    /// <summary>
    /// Formats raw bytes with Go's <c>%q</c> verb: valid UTF-8 sequences are
    /// shown as characters and invalid bytes as <c>\xNN</c>.
    /// </summary>
    public static string QuoteUtf8(ReadOnlySpan<byte> bytes)
    {
        var b = new StringBuilder(bytes.Length + 2);
        Span<char> units = stackalloc char[2];
        b.Append('"');
        while (!bytes.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(bytes, out Rune rune, out int consumed) != OperationStatus.Done)
            {
                b.Append("\\x").Append(bytes[0].ToString("x2", CultureInfo.InvariantCulture));
                bytes = bytes.Slice(1);
                continue;
            }

            int length = rune.EncodeToUtf16(units);
            if (length == 1 && (units[0] == '"' || units[0] == '\\'))
            {
                b.Append('\\').Append(units[0]);
            }
            else if (length == 1)
            {
                AppendEscaped(b, units[0]);
            }
            else
            {
                b.Append(units.Slice(0, length));
            }

            bytes = bytes.Slice(consumed);
        }

        return b.Append('"').ToString();
    }

    /// <summary>Formats a character with Go's <c>%q</c> verb for runes (<c>strconv.QuoteRune</c>).</summary>
    public static string QuoteRune(string rune)
    {
        var b = new StringBuilder(rune.Length + 2);
        b.Append('\'');
        if (rune.Length == 1)
        {
            char c = rune[0];
            if (c == '\'' || c == '\\')
            {
                b.Append('\\').Append(c);
            }
            else
            {
                AppendEscaped(b, c);
            }
        }
        else
        {
            b.Append(rune);
        }

        return b.Append('\'').ToString();
    }

    /// <summary>Formats a single UTF-16 code unit with Go's <c>%q</c> verb for runes.</summary>
    public static string QuoteRune(char c) => QuoteRune(new string(c, 1));

    /// <summary>Returns the character (one code unit, or a surrogate pair) that starts at <paramref name="index"/>.</summary>
    public static string RuneAt(string s, int index)
    {
        if (char.IsHighSurrogate(s[index]) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
        {
            return s.Substring(index, 2);
        }

        return s.Substring(index, 1);
    }

    private static void AppendEscaped(StringBuilder b, char c)
    {
        switch (c)
        {
            case '\a': b.Append("\\a"); break;
            case '\b': b.Append("\\b"); break;
            case '\f': b.Append("\\f"); break;
            case '\n': b.Append("\\n"); break;
            case '\r': b.Append("\\r"); break;
            case '\t': b.Append("\\t"); break;
            case '\v': b.Append("\\v"); break;
            default:
                if (c < 0x20 || c == 0x7F)
                {
                    b.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                }
                else if (c < 0x7F)
                {
                    b.Append(c);
                }
                else if (char.IsSurrogate(c))
                {
                    // A lone surrogate cannot come from valid UTF-8; Go would print the
                    // replacement character for the equivalent invalid byte sequence.
                    b.Append((char)0xFFFD);
                }
                else if (!IsGraphic(c))
                {
                    b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    b.Append(c);
                }

                break;
        }
    }

    // Go's strconv.IsPrint for non-ASCII BMP characters: letters, marks,
    // numbers, punctuation and symbols. Spaces other than U+0020 are not printable.
    private static bool IsGraphic(char c) => char.GetUnicodeCategory(c) switch
    {
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter => true,
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark => true,
        UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber => true,
        UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation => true,
        UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol
            or UnicodeCategory.OtherSymbol => true,
        _ => false,
    };

    /// <summary>Formats an integer with the invariant culture.</summary>
    public static string Str(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats an integer with the invariant culture.</summary>
    public static string Str(long value) => value.ToString(CultureInfo.InvariantCulture);
}
