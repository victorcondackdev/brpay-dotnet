using System.Text;
using BrPay.Internal;

namespace BrPay.Documents;

/// <summary>
/// Validation of the CNPJ (Cadastro Nacional da Pessoa Jurídica), numeric or alphanumeric.
/// </summary>
/// <remarks>
/// In the alphanumeric format (issued from July 2026) the first 12
/// characters may be digits or uppercase letters A–Z; the last two (check
/// digits) are always numeric. The check digits use the classic modulo-11
/// weights over the value <c>(ASCII code − 48)</c> of every character, which
/// keeps purely numeric CNPJs backward compatible.
/// </remarks>
public static class Cnpj
{
    /// <summary>The number of characters of a bare CNPJ.</summary>
    public const int Length = 14;

    /// <summary>Validates a CNPJ and returns the reason it is invalid, or <see cref="DocumentError.None"/>.</summary>
    /// <param name="input">The CNPJ; punctuation is allowed and letters may be lowercase.</param>
    /// <returns>The validation outcome.</returns>
    public static DocumentError Check(string input)
    {
        string c = Document.Clean(input);
        if (Encoding.UTF8.GetByteCount(c) != Length)
        {
            // Lengths are counted in UTF-8 bytes, like the Go implementation.
            return DocumentError.Length;
        }

        for (int i = 0; i < 12; i++)
        {
            char ch = c[i];
            if (!AsciiText.IsDigit(ch) && !(ch >= 'A' && ch <= 'Z'))
            {
                return DocumentError.Characters;
            }
        }

        if (!AsciiText.IsDigit(c[12]) || !AsciiText.IsDigit(c[13]))
        {
            return DocumentError.Characters;
        }

        if (Document.AllSame(c))
        {
            return DocumentError.Repeated;
        }

        (int d1, int d2) = Digits(c.AsSpan(0, 12));
        return c[12] - '0' == d1 && c[13] - '0' == d2 ? DocumentError.None : DocumentError.CheckDigit;
    }

    /// <summary>Reports whether a CNPJ is valid.</summary>
    /// <param name="input">The CNPJ; punctuation is allowed.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValid(string input) => Check(input) == DocumentError.None;

    /// <summary>Validates a CNPJ.</summary>
    /// <param name="input">The CNPJ; punctuation is allowed.</param>
    /// <exception cref="DocumentException">The CNPJ is invalid.</exception>
    public static void Validate(string input)
    {
        DocumentError error = Check(input);
        if (error != DocumentError.None)
        {
            throw new DocumentException(DocumentKind.Cnpj, error);
        }
    }

    /// <summary>Reports whether a valid CNPJ uses the alphanumeric format (contains at least one letter).</summary>
    /// <param name="input">The CNPJ; punctuation is allowed.</param>
    /// <returns><see langword="true"/> for a valid alphanumeric CNPJ.</returns>
    public static bool IsAlphanumeric(string input)
    {
        string c = Document.Clean(input);
        if (Check(c) != DocumentError.None)
        {
            return false;
        }

        for (int i = 0; i < 12; i++)
        {
            if (c[i] >= 'A' && c[i] <= 'Z')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Computes the two check digits of a 12-character CNPJ base, numeric or alphanumeric.</summary>
    /// <param name="baseChars">The first twelve characters: ASCII digits or uppercase letters.</param>
    /// <returns>The first and second check digits (0–9).</returns>
    /// <exception cref="ArgumentException"><paramref name="baseChars"/> is not twelve digits or uppercase letters.</exception>
    public static (int First, int Second) ComputeCheckDigits(ReadOnlySpan<char> baseChars)
    {
        if (baseChars.Length != 12)
        {
            throw new ArgumentException("A CNPJ base must be 12 characters.", nameof(baseChars));
        }

        foreach (char ch in baseChars)
        {
            if (!AsciiText.IsDigit(ch) && !(ch >= 'A' && ch <= 'Z'))
            {
                throw new ArgumentException("A CNPJ base must contain only digits and uppercase letters.", nameof(baseChars));
            }
        }

        return Digits(baseChars);
    }

    private static (int First, int Second) Digits(ReadOnlySpan<char> b)
    {
        int d1 = Digit(b, extra: -1);
        return (d1, Digit(b, extra: d1));
    }

    // Weights 2..9 cycling from the right. When extra >= 0 it is appended as
    // the last (rightmost) digit before weighting.
    private static int Digit(ReadOnlySpan<char> b, int extra)
    {
        int sum = 0;
        int w = 2;
        if (extra >= 0)
        {
            sum += extra * w;
            w++;
        }

        for (int i = b.Length - 1; i >= 0; i--)
        {
            sum += (b[i] - '0') * w;
            w++;
            if (w > 9)
            {
                w = 2;
            }
        }

        int r = sum % 11;
        return r < 2 ? 0 : 11 - r;
    }
}
