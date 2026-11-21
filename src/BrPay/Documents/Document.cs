using System.Text;

namespace BrPay.Documents;

/// <summary>
/// Detects, formats and masks CPFs and CNPJs, including the alphanumeric
/// CNPJ that the Receita Federal starts issuing in July 2026.
/// </summary>
public static class Document
{
    /// <summary>
    /// Removes the usual punctuation (<c>.</c>, <c>-</c>, <c>/</c> and spaces)
    /// and uppercases ASCII letters. Any other character is kept so that
    /// validation can reject it. It does not validate.
    /// </summary>
    /// <param name="input">The raw input.</param>
    /// <returns>The cleaned input.</returns>
    public static string Clean(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var b = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            switch (c)
            {
                case >= 'a' and <= 'z':
                    b.Append((char)(c - ('a' - 'A')));
                    break;
                case '.' or '-' or '/' or ' ':
                    break;
                default:
                    b.Append(c);
                    break;
            }
        }

        return b.ToString();
    }

    /// <summary>Returns the type of a valid document, or <see cref="DocumentKind.Unknown"/>.</summary>
    /// <param name="input">The document, punctuation allowed.</param>
    /// <returns>The detected type.</returns>
    public static DocumentKind Detect(string input)
    {
        string c = Clean(input);
        if (Cpf.Check(c) == DocumentError.None)
        {
            return DocumentKind.Cpf;
        }

        return Cnpj.Check(c) == DocumentError.None ? DocumentKind.Cnpj : DocumentKind.Unknown;
    }

    /// <summary>
    /// Returns the canonical punctuated form (<c>000.000.000-00</c> or
    /// <c>AA.AAA.AAA/AAAA-00</c>). Invalid input is returned cleaned but unformatted.
    /// </summary>
    /// <param name="input">The document, punctuation allowed.</param>
    /// <returns>The formatted document.</returns>
    public static string Format(string input)
    {
        string c = Clean(input);
        if (Cpf.Check(c) == DocumentError.None)
        {
            return c.Substring(0, 3) + "." + c.Substring(3, 3) + "." + c.Substring(6, 3) + "-" + c.Substring(9, 2);
        }

        if (Cnpj.Check(c) == DocumentError.None)
        {
            return c.Substring(0, 2) + "." + c.Substring(2, 3) + "." + c.Substring(5, 3) + "/" + c.Substring(8, 4) + "-" + c.Substring(12, 2);
        }

        return c;
    }

    /// <summary>
    /// Hides the middle of a document for logs, following the LGPD-friendly
    /// pattern used by the Pix DICT: <c>***.456.789-**</c> for a CPF and
    /// <c>12.***.***/0001-**</c> for a CNPJ. Invalid input is fully masked (one <c>*</c> per UTF-8 byte).
    /// </summary>
    /// <param name="input">The document, punctuation allowed.</param>
    /// <returns>The masked document.</returns>
    public static string Mask(string input)
    {
        string c = Clean(input);
        if (Cpf.Check(c) == DocumentError.None)
        {
            return "***." + c.Substring(3, 3) + "." + c.Substring(6, 3) + "-**";
        }

        if (Cnpj.Check(c) == DocumentError.None)
        {
            return c.Substring(0, 2) + ".***.***/" + c.Substring(8, 4) + "-**";
        }

        return new string('*', Encoding.UTF8.GetByteCount(c));
    }

    internal static bool AllSame(ReadOnlySpan<char> s)
    {
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] != s[0])
            {
                return false;
            }
        }

        return true;
    }
}
