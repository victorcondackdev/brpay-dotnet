using System.Text;
using BrPay.Internal;

namespace BrPay.Documents;

/// <summary>Validation of the CPF (Cadastro de Pessoas Físicas), 11 digits with two modulo-11 check digits.</summary>
public static class Cpf
{
    /// <summary>The number of characters of a bare CPF.</summary>
    public const int Length = 11;

    /// <summary>Validates a CPF and returns the reason it is invalid, or <see cref="DocumentError.None"/>.</summary>
    /// <param name="input">The CPF; punctuation (<c>.</c>, <c>-</c>, <c>/</c>, spaces) is allowed.</param>
    /// <returns>The validation outcome.</returns>
    public static DocumentError Check(string input)
    {
        string c = Document.Clean(input);
        if (Encoding.UTF8.GetByteCount(c) != Length)
        {
            // Lengths are counted in UTF-8 bytes, like the Go implementation.
            return DocumentError.Length;
        }

        foreach (char ch in c)
        {
            if (!AsciiText.IsDigit(ch))
            {
                return DocumentError.Characters;
            }
        }

        if (Document.AllSame(c))
        {
            return DocumentError.Repeated;
        }

        (int d1, int d2) = Digits(c.AsSpan(0, 9));
        return c[9] - '0' == d1 && c[10] - '0' == d2 ? DocumentError.None : DocumentError.CheckDigit;
    }

    /// <summary>Reports whether a CPF is valid.</summary>
    /// <param name="input">The CPF; punctuation is allowed.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValid(string input) => Check(input) == DocumentError.None;

    /// <summary>Validates a CPF.</summary>
    /// <param name="input">The CPF; punctuation is allowed.</param>
    /// <exception cref="DocumentException">The CPF is invalid.</exception>
    public static void Validate(string input)
    {
        DocumentError error = Check(input);
        if (error != DocumentError.None)
        {
            throw new DocumentException(DocumentKind.Cpf, error);
        }
    }

    /// <summary>Computes the two check digits of a 9-digit CPF base.</summary>
    /// <param name="digits">The first nine digits.</param>
    /// <returns>The first and second check digits (0–9).</returns>
    /// <exception cref="ArgumentException"><paramref name="digits"/> is not nine ASCII digits.</exception>
    public static (int First, int Second) ComputeCheckDigits(ReadOnlySpan<char> digits)
    {
        if (digits.Length != 9 || !AsciiText.IsDigits(digits))
        {
            throw new ArgumentException("A CPF base must be 9 ASCII digits.", nameof(digits));
        }

        return Digits(digits);
    }

    private static (int First, int Second) Digits(ReadOnlySpan<char> b)
    {
        int sum1 = 0;
        int sum2 = 0;
        for (int i = 0; i < 9; i++)
        {
            int v = b[i] - '0';
            sum1 += v * (10 - i);
            sum2 += v * (11 - i);
        }

        int d1 = Reduce(sum1);
        sum2 += d1 * 2;
        return (d1, Reduce(sum2));
    }

    private static int Reduce(int sum)
    {
        int r = sum % 11;
        return r < 2 ? 0 : 11 - r;
    }
}
