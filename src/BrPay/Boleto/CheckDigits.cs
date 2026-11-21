using BrPay.Internal;

namespace BrPay.Boleto;

/// <summary>The FEBRABAN check-digit algorithms used by boletos.</summary>
public static class CheckDigits
{
    /// <summary>
    /// Modulo 10: weights 2, 1, 2, 1… from right to left, the digits of each
    /// product are summed, and the check digit is <c>(10 − sum mod 10) mod 10</c>.
    /// </summary>
    /// <param name="digits">ASCII digits.</param>
    /// <returns>The check digit, <c>'0'</c>–<c>'9'</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="digits"/> contains a non-digit.</exception>
    public static char Mod10(ReadOnlySpan<char> digits)
    {
        RequireDigits(digits);
        return Mod10Core(digits);
    }

    /// <summary>
    /// The bancário general check digit (DAC): modulo 11 with weights 2–9
    /// from the right; <c>11 − (sum mod 11)</c>, with 0, 10 and 11 replaced by 1.
    /// </summary>
    /// <param name="digits">ASCII digits.</param>
    /// <returns>The check digit, <c>'1'</c>–<c>'9'</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="digits"/> contains a non-digit.</exception>
    public static char Mod11Bancario(ReadOnlySpan<char> digits)
    {
        RequireDigits(digits);
        return Mod11BancarioCore(digits);
    }

    /// <summary>
    /// The arrecadação modulo-11 check digit: remainder 0 or 1 gives 0,
    /// remainder 10 gives 1, otherwise <c>11 − remainder</c>.
    /// </summary>
    /// <param name="digits">ASCII digits.</param>
    /// <returns>The check digit, <c>'0'</c>–<c>'9'</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="digits"/> contains a non-digit.</exception>
    public static char Mod11Arrecadacao(ReadOnlySpan<char> digits)
    {
        RequireDigits(digits);
        return Mod11ArrecadacaoCore(digits);
    }

    internal static char Mod10Core(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        int w = 2;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int p = (digits[i] - '0') * w;
            sum += (p / 10) + (p % 10);
            w = 3 - w;
        }

        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    internal static char Mod11BancarioCore(ReadOnlySpan<char> digits)
    {
        int dv = 11 - (Sum11(digits) % 11);
        if (dv == 0 || dv > 9)
        {
            dv = 1;
        }

        return (char)('0' + dv);
    }

    internal static char Mod11ArrecadacaoCore(ReadOnlySpan<char> digits)
    {
        int r = Sum11(digits) % 11;
        return r switch
        {
            0 or 1 => '0',
            10 => '1',
            _ => (char)('0' + 11 - r),
        };
    }

    private static int Sum11(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        int w = 2;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            sum += (digits[i] - '0') * w;
            w++;
            if (w > 9)
            {
                w = 2;
            }
        }

        return sum;
    }

    private static void RequireDigits(ReadOnlySpan<char> digits)
    {
        foreach (char c in digits)
        {
            if (!AsciiText.IsDigit(c))
            {
                throw new ArgumentException("Check digits are computed over ASCII digits only.", nameof(digits));
            }
        }
    }
}
