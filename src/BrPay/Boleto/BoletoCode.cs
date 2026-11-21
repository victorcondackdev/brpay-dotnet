using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using BrPay.Internal;

namespace BrPay.Boleto;

/// <summary>A parsed and fully validated boleto: barcode and linha digitável of either family.</summary>
/// <remarks>
/// <para>Two families share the 44-digit barcode size but differ in everything else:</para>
/// <list type="bullet">
/// <item><description>Bancário (bank slips): a 47-digit linha digitável in five fields. Fields 1–3 carry a modulo-10 check digit, field 4 is the general modulo-11 check digit (DAC) and field 5 holds the due-date factor and the amount.</description></item>
/// <item><description>Arrecadação (utility bills, taxes, fines): barcodes start with 8 and the 48-digit linha digitável is four blocks of eleven barcode digits, each followed by its own check digit. The modulus (10 or 11) is chosen by the third digit.</description></item>
/// </list>
/// <para>Exactly one of <see cref="Bancario"/> and <see cref="Arrecadacao"/> is set, according to <see cref="Type"/>.</para>
/// </remarks>
public sealed record BoletoCode
{
    /// <summary>The length of a barcode of either family.</summary>
    public const int BarcodeLength = 44;

    /// <summary>The length of a bancário linha digitável.</summary>
    public const int BancarioLineLength = 47;

    /// <summary>The length of an arrecadação linha digitável.</summary>
    public const int ArrecadacaoLineLength = 48;

    /// <summary>
    /// The maximum number of candidates returned by <see cref="SuggestFix"/>,
    /// above the worst case for a single typo (43 candidates, for a bancário
    /// typo in field 5), so the correct line is never cut off.
    /// </summary>
    public const int MaxSuggestions = 50;

    private const long MaxBancarioAmount = 9_999_999_999;
    private const long MaxArrecadacaoAmount = 99_999_999_999;
    private const int BancarioFreeLength = 25;
    private const int ArrecadacaoRestLength = 29;

    // [start, end) of the five fields of a bancário line, and of the four arrecadação blocks.
    private static ReadOnlySpan<byte> BancarioRanges => new byte[] { 0, 10, 10, 21, 21, 32, 32, 33, 33, 47 };
    private static ReadOnlySpan<byte> ArrecadacaoRanges => new byte[] { 0, 12, 12, 24, 24, 36, 36, 48 };

    // Start and check-digit index of the three bancário fields with a modulo-10 digit.
    private static ReadOnlySpan<byte> BancarioFields => new byte[] { 0, 9, 10, 20, 21, 31 };

    /// <summary>The boleto family.</summary>
    public BoletoType Type { get; init; }

    /// <summary>The 44-digit barcode.</summary>
    public string Barcode { get; init; } = string.Empty;

    /// <summary>The linha digitável as bare digits (47 or 48).</summary>
    public string DigitableLine { get; init; } = string.Empty;

    /// <summary>
    /// The amount field in cents. For arrecadação codes whose value is a
    /// reference (see <see cref="ArrecadacaoDetails.ValueIsReference"/>) it
    /// holds the raw reference value instead of money.
    /// </summary>
    public long AmountCents { get; init; }

    /// <summary>The amount in reais, for display. Transport and arithmetic should use <see cref="AmountCents"/>.</summary>
    public decimal Amount => AmountCents / 100m;

    /// <summary>The bank-slip specific fields, or <see langword="null"/>.</summary>
    public BancarioDetails? Bancario { get; init; }

    /// <summary>The utility-bill specific fields, or <see langword="null"/>.</summary>
    public ArrecadacaoDetails? Arrecadacao { get; init; }

    /// <summary>
    /// Returns the linha digitável in its printed form:
    /// <c>AAAAA.AAAAA BBBBB.BBBBBB CCCCC.CCCCCC D EEEEEEEEEEEEEE</c> for bancário and
    /// <c>XXXXXXXXXXX-D XXXXXXXXXXX-D XXXXXXXXXXX-D XXXXXXXXXXX-D</c> for arrecadação.
    /// </summary>
    /// <returns>The formatted line.</returns>
    public string Formatted() => FormatLine(DigitableLine);

    /// <summary>Returns <see cref="Formatted"/>.</summary>
    /// <returns>The formatted line.</returns>
    public override string ToString() => Formatted();

    /// <summary>
    /// Parses a barcode (44 digits) or a linha digitável (47 digits for
    /// bancário, 48 for arrecadação). Spaces, tabs, newlines, dots and hyphens
    /// are ignored. Every check digit is verified.
    /// </summary>
    /// <param name="input">The barcode or line.</param>
    /// <returns>The parsed boleto.</returns>
    /// <exception cref="CheckDigitException">A check digit does not match.</exception>
    /// <exception cref="BoletoException">The input is malformed.</exception>
    public static BoletoCode Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ParseCore(input, out BoletoException? error) ?? throw error!;
    }

    /// <summary>Parses a barcode or linha digitável without throwing.</summary>
    /// <param name="input">The barcode or line.</param>
    /// <param name="boleto">The parsed boleto, when valid.</param>
    /// <returns><see langword="true"/> when the input is valid.</returns>
    public static bool TryParse(string? input, [NotNullWhen(true)] out BoletoCode? boleto)
    {
        if (input is null)
        {
            boleto = null;
            return false;
        }

        boleto = ParseCore(input, out _);
        return boleto is not null;
    }

    /// <summary>Parses a barcode or linha digitável, reporting the failure instead of throwing it.</summary>
    /// <param name="input">The barcode or line.</param>
    /// <param name="boleto">The parsed boleto, when valid.</param>
    /// <param name="error">The reason parsing failed, when invalid.</param>
    /// <returns><see langword="true"/> when the input is valid.</returns>
    public static bool TryParse(string input, [NotNullWhen(true)] out BoletoCode? boleto, [NotNullWhen(false)] out BoletoException? error)
    {
        ArgumentNullException.ThrowIfNull(input);
        boleto = ParseCore(input, out error);
        if (boleto is not null)
        {
            return true;
        }

        error ??= BoletoException.Of(BoletoError.Length, "invalid");
        return false;
    }

    /// <summary>Reports whether the input is a valid barcode or linha digitável.</summary>
    /// <param name="input">The barcode or line.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValid(string? input) => TryParse(input, out _);

    /// <summary>Validates a barcode or linha digitável.</summary>
    /// <param name="input">The barcode or line.</param>
    /// <exception cref="BoletoException">The input is invalid; the same exception <see cref="Parse"/> would throw.</exception>
    public static void Validate(string input) => _ = Parse(input);

    /// <summary>Converts a 44-digit barcode of either family into its linha digitável (bare digits), validating it first.</summary>
    /// <param name="barcode">The barcode; separators are allowed.</param>
    /// <returns>The linha digitável.</returns>
    /// <exception cref="BoletoException">The barcode is invalid.</exception>
    public static string BarcodeToLine(string barcode)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        string d = Clean(barcode, out BoletoException? error) ?? throw error!;
        if (d.Length != BarcodeLength)
        {
            throw BoletoException.Of(BoletoError.Length, "got " + AsciiText.Str(d.Length) + " digits, want " + AsciiText.Str(BarcodeLength));
        }

        return (ParseBarcode(d, out error) ?? throw error!).DigitableLine;
    }

    /// <summary>Converts a linha digitável of either family (47 or 48 digits, punctuation allowed) into its 44-digit barcode, validating it first.</summary>
    /// <param name="line">The linha digitável.</param>
    /// <returns>The barcode.</returns>
    /// <exception cref="BoletoException">The line is invalid.</exception>
    public static string LineToBarcode(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string d = Clean(line, out BoletoException? error) ?? throw error!;
        if (d.Length != BancarioLineLength && d.Length != ArrecadacaoLineLength)
        {
            throw BoletoException.Of(BoletoError.Length, "got " + AsciiText.Str(d.Length) + " digits, want " + AsciiText.Str(BancarioLineLength) + " or " + AsciiText.Str(ArrecadacaoLineLength));
        }

        return (ParseLine(d, out error) ?? throw error!).Barcode;
    }

    /// <summary>
    /// Builds a bank slip in Brazilian reais, computing every check digit.
    /// </summary>
    /// <param name="bankCode">The 3-digit COMPE code.</param>
    /// <param name="dueDate">The due date, or <see langword="null"/> for "no due date" (factor 0000).</param>
    /// <param name="amountCents">The amount in cents; must fit ten digits.</param>
    /// <param name="freeField">The 25-digit bank-specific field.</param>
    /// <returns>The boleto.</returns>
    /// <exception cref="BoletoException">A parameter is invalid.</exception>
    public static BoletoCode CreateBancario(string bankCode, DateOnly? dueDate, long amountCents, string freeField)
    {
        ArgumentNullException.ThrowIfNull(bankCode);
        ArgumentNullException.ThrowIfNull(freeField);
        if (bankCode.Length != 3 || !AsciiText.IsDigits(bankCode))
        {
            throw BoletoException.Of(BoletoError.BankCode, AsciiText.Quote(bankCode));
        }

        if (bankCode[0] == '8')
        {
            throw BoletoException.Of(BoletoError.ProductId, "bank codes starting with 8 are reserved for arrecadação");
        }

        if (amountCents < 0 || amountCents > MaxBancarioAmount)
        {
            throw BoletoException.Of(BoletoError.Amount, AsciiText.Str(amountCents));
        }

        if (freeField.Length != BancarioFreeLength || !AsciiText.IsDigits(freeField))
        {
            throw BoletoException.Of(BoletoError.FreeField, "want " + AsciiText.Str(BancarioFreeLength) + " digits");
        }

        int factor = dueDate is null ? 0 : DueDateFactor.FromDate(dueDate.Value);
        string body = bankCode + "9"
            + factor.ToString("D4", CultureInfo.InvariantCulture)
            + amountCents.ToString("D10", CultureInfo.InvariantCulture)
            + freeField;
        string code = body.Substring(0, 4) + CheckDigits.Mod11BancarioCore(body) + body.Substring(4);
        return BuildBancario(code);
    }

    /// <summary>Builds an arrecadação code, computing every check digit.</summary>
    /// <param name="segment">The segment.</param>
    /// <param name="valueType">6 or 7: modulo 10; 8 or 9: modulo 11. Types 7 and 9 mean the value is a reference.</param>
    /// <param name="value">The value, at most 11 digits.</param>
    /// <param name="rest">The 29-digit tail: the company or agency identification followed by the free field.</param>
    /// <returns>The boleto.</returns>
    /// <exception cref="BoletoException">A parameter is invalid.</exception>
    public static BoletoCode CreateArrecadacao(ArrecadacaoSegment segment, int valueType, long value, string rest)
    {
        ArgumentNullException.ThrowIfNull(rest);
        if (segment.GetDescription() is null)
        {
            throw BoletoException.Of(BoletoError.Segment, AsciiText.Str((int)segment));
        }

        if (valueType < 6 || valueType > 9)
        {
            throw BoletoException.Of(BoletoError.ValueType, AsciiText.Str(valueType));
        }

        if (value < 0 || value > MaxArrecadacaoAmount)
        {
            throw BoletoException.Of(BoletoError.Amount, AsciiText.Str(value));
        }

        if (rest.Length != ArrecadacaoRestLength || !AsciiText.IsDigits(rest))
        {
            throw BoletoException.Of(BoletoError.FreeField, "want " + AsciiText.Str(ArrecadacaoRestLength) + " digits");
        }

        string body = "8" + AsciiText.Str((int)segment) + AsciiText.Str(valueType)
            + value.ToString("D11", CultureInfo.InvariantCulture) + rest;
        bool mod11 = valueType >= 8;
        char dv = mod11 ? CheckDigits.Mod11ArrecadacaoCore(body) : CheckDigits.Mod10Core(body);
        string code = body.Substring(0, 3) + dv + body.Substring(3);
        return BuildArrecadacao(code, mod11);
    }

    /// <summary>
    /// Proposes corrections for a linha digitável (47 or 48 digits,
    /// punctuation allowed) that contains exactly one mistyped digit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It first locates the typo using the per-field check digits, then
    /// tries the nine other values at every plausible position and keeps the
    /// candidates that make the whole line valid, including the general check digit.
    /// </para>
    /// <para>
    /// A single typo usually yields one to a few candidates; check digits
    /// cannot always tell them apart, so present them to the user to choose
    /// from. Candidates are bare digits, ordered by position and then by
    /// digit, at most <see cref="MaxSuggestions"/>. The output is identical to
    /// the Go implementation.
    /// </para>
    /// </remarks>
    /// <param name="line">The mistyped line.</param>
    /// <returns>The candidates; empty for valid lines, barcodes and malformed input.</returns>
    public static IReadOnlyList<string> SuggestFix(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string? d = Clean(line, out _);
        if (d is null || (d.Length != BancarioLineLength && d.Length != ArrecadacaoLineLength))
        {
            return Array.Empty<string>();
        }

        if (ParseLine(d, out _) is not null)
        {
            return Array.Empty<string>();
        }

        var output = new List<string>();
        char[] buf = d.ToCharArray();
        foreach (int i in SearchPositions(d))
        {
            char orig = buf[i];
            for (char c = '0'; c <= '9'; c++)
            {
                if (c == orig)
                {
                    continue;
                }

                buf[i] = c;
                string candidate = new string(buf);
                if (ParseLine(candidate, out _) is not null)
                {
                    output.Add(candidate);
                    if (output.Count == MaxSuggestions)
                    {
                        return output;
                    }
                }
            }

            buf[i] = orig;
        }

        return output;
    }

    internal static string FormatLine(string d) => d.Length switch
    {
        BancarioLineLength => d.Substring(0, 5) + "." + d.Substring(5, 5) + " " + d.Substring(10, 5) + "." + d.Substring(15, 6) + " "
            + d.Substring(21, 5) + "." + d.Substring(26, 6) + " " + d.Substring(32, 1) + " " + d.Substring(33, 14),
        ArrecadacaoLineLength => d.Substring(0, 11) + "-" + d.Substring(11, 1) + " " + d.Substring(12, 11) + "-" + d.Substring(23, 1) + " "
            + d.Substring(24, 11) + "-" + d.Substring(35, 1) + " " + d.Substring(36, 11) + "-" + d.Substring(47, 1),
        _ => d,
    };

    internal static BoletoCode? ParseCore(string input, out BoletoException? error)
    {
        string? d = Clean(input, out error);
        if (d is null)
        {
            return null;
        }

        switch (d.Length)
        {
            case BarcodeLength:
                return ParseBarcode(d, out error);
            case BancarioLineLength:
            case ArrecadacaoLineLength:
                return ParseLine(d, out error);
            default:
                error = LengthError(d.Length);
                return null;
        }
    }

    // Strips the accepted separators and rejects anything else. It stops
    // early once the input exceeds the longest valid form, bounding work on
    // hostile input.
    private static string? Clean(string s, out BoletoException? error)
    {
        var b = new StringBuilder(ArrecadacaoLineLength);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= '0' && c <= '9')
            {
                if (b.Length == ArrecadacaoLineLength)
                {
                    error = BoletoException.Of(BoletoError.Length, "more than " + AsciiText.Str(ArrecadacaoLineLength) + " digits");
                    return null;
                }

                b.Append(c);
            }
            else if (c != ' ' && c != '.' && c != '-' && c != '\t' && c != '\n' && c != '\r')
            {
                int offset = Encoding.UTF8.GetByteCount(s.AsSpan(0, i));
                error = new BoletoException(
                    BoletoError.Characters,
                    BoletoException.MessageFor(BoletoError.Characters) + " " + AsciiText.QuoteRune(AsciiText.RuneAt(s, i)) + " at byte offset " + AsciiText.Str(offset));
                return null;
            }
        }

        error = null;
        return b.ToString();
    }

    private static BoletoException LengthError(int n) =>
        BoletoException.Of(BoletoError.Length, "got " + AsciiText.Str(n) + " digits, want 44, 47 or 48");

    private static BoletoCode? ParseBarcode(string code, out BoletoException? error) =>
        code[0] == '8' ? ParseArrecadacaoBarcode(code, out error) : ParseBancarioBarcode(code, out error);

    private static BoletoCode? ParseLine(string line, out BoletoException? error)
    {
        switch (line.Length)
        {
            case BancarioLineLength:
                return ParseBancarioLine(line, out error);
            case ArrecadacaoLineLength:
                return ParseArrecadacaoLine(line, out error);
            default:
                error = LengthError(line.Length);
                return null;
        }
    }

    private static BoletoCode? ParseBancarioBarcode(string code, out BoletoException? error)
    {
        if (code[0] == '8')
        {
            error = BoletoException.Of(BoletoError.ProductId, "bancário codes cannot start with 8");
            return null;
        }

        char want = CheckDigits.Mod11BancarioCore(code.Substring(0, 4) + code.Substring(5));
        if (code[4] != want)
        {
            error = new CheckDigitException(CheckDigitException.FieldGeneral, want, code[4]);
            return null;
        }

        error = null;
        return BuildBancario(code);
    }

    private static BoletoCode? ParseBancarioLine(string line, out BoletoException? error)
    {
        if (line[0] == '8')
        {
            error = BoletoException.Of(BoletoError.ProductId, "bancário codes cannot start with 8");
            return null;
        }

        ReadOnlySpan<byte> fields = BancarioFields;
        for (int i = 0; i < 3; i++)
        {
            int start = fields[2 * i];
            int dvIndex = fields[(2 * i) + 1];
            char want = CheckDigits.Mod10Core(line.AsSpan(start, dvIndex - start));
            if (line[dvIndex] != want)
            {
                error = new CheckDigitException(i + 1, want, line[dvIndex]);
                return null;
            }
        }

        string code = line.Substring(0, 4) + line.Substring(32, 1) + line.Substring(33, 14)
            + line.Substring(4, 5) + line.Substring(10, 10) + line.Substring(21, 10);
        return ParseBancarioBarcode(code, out error);
    }

    // Assembles a boleto from a barcode whose DAC is known to be valid.
    private static BoletoCode BuildBancario(string code)
    {
        string f1 = code.Substring(0, 4) + code.Substring(19, 5);
        string f2 = code.Substring(24, 10);
        string f3 = code.Substring(34, 10);
        string line = f1 + CheckDigits.Mod10Core(f1) + f2 + CheckDigits.Mod10Core(f2) + f3 + CheckDigits.Mod10Core(f3) + code.Substring(4, 15);
        string bank = code.Substring(0, 3);
        return new BoletoCode
        {
            Type = BoletoType.Bancario,
            Barcode = code,
            DigitableLine = line,
            AmountCents = long.Parse(code.AsSpan(9, 10), NumberStyles.None, CultureInfo.InvariantCulture),
            Bancario = new BancarioDetails
            {
                BankCode = bank,
                BankName = Banks.GetName(bank),
                Currency = code[3] - '0',
                Factor = int.Parse(code.AsSpan(5, 4), NumberStyles.None, CultureInfo.InvariantCulture),
                FreeField = code.Substring(19, 25),
            },
        };
    }

    // Returns whether the value-type digit selects modulo 11, or null when it is invalid.
    private static bool? IsMod11(char valueType) => valueType switch
    {
        '6' or '7' => false,
        '8' or '9' => true,
        _ => null,
    };

    private static char ArrecadacaoDigit(ReadOnlySpan<char> digits, bool mod11) =>
        mod11 ? CheckDigits.Mod11ArrecadacaoCore(digits) : CheckDigits.Mod10Core(digits);

    private static BoletoException ValueTypeError(char c) =>
        BoletoException.Of(BoletoError.ValueType, AsciiText.QuoteRune(c));

    private static BoletoCode? ParseArrecadacaoBarcode(string code, out BoletoException? error)
    {
        if (code[0] != '8')
        {
            error = BoletoException.Of(BoletoError.ProductId, "arrecadação codes start with 8");
            return null;
        }

        bool? mod11 = IsMod11(code[2]);
        if (mod11 is null)
        {
            error = ValueTypeError(code[2]);
            return null;
        }

        char want = ArrecadacaoDigit(code.Substring(0, 3) + code.Substring(4), mod11.Value);
        if (code[3] != want)
        {
            error = new CheckDigitException(CheckDigitException.FieldGeneral, want, code[3]);
            return null;
        }

        error = null;
        return BuildArrecadacao(code, mod11.Value);
    }

    private static BoletoCode? ParseArrecadacaoLine(string line, out BoletoException? error)
    {
        if (line[0] != '8')
        {
            error = BoletoException.Of(BoletoError.ProductId, "arrecadação codes start with 8");
            return null;
        }

        bool? mod11 = IsMod11(line[2]);
        if (mod11 is null)
        {
            error = ValueTypeError(line[2]);
            return null;
        }

        var code = new StringBuilder(BarcodeLength);
        ReadOnlySpan<byte> ranges = ArrecadacaoRanges;
        for (int i = 0; i < 4; i++)
        {
            int start = ranges[2 * i];
            int end = ranges[(2 * i) + 1];
            ReadOnlySpan<char> block = line.AsSpan(start, end - start - 1);
            char got = line[end - 1];
            char want = ArrecadacaoDigit(block, mod11.Value);
            if (got != want)
            {
                error = new CheckDigitException(i + 1, want, got);
                return null;
            }

            code.Append(block);
        }

        return ParseArrecadacaoBarcode(code.ToString(), out error);
    }

    // Assembles a boleto from a barcode whose general check digit is known to be valid.
    private static BoletoCode BuildArrecadacao(string code, bool mod11)
    {
        var line = new StringBuilder(ArrecadacaoLineLength);
        for (int i = 0; i < BarcodeLength; i += 11)
        {
            ReadOnlySpan<char> block = code.AsSpan(i, 11);
            line.Append(block).Append(ArrecadacaoDigit(block, mod11));
        }

        int vt = code[2] - '0';
        var segment = (ArrecadacaoSegment)(code[1] - '0');
        bool cnpj = segment == ArrecadacaoSegment.Cnpj;
        return new BoletoCode
        {
            Type = BoletoType.Arrecadacao,
            Barcode = code,
            DigitableLine = line.ToString(),
            AmountCents = long.Parse(code.AsSpan(4, 11), NumberStyles.None, CultureInfo.InvariantCulture),
            Arrecadacao = new ArrecadacaoDetails
            {
                Segment = segment,
                ValueType = vt,
                ValueIsReference = vt == 7 || vt == 9,
                Modulus = vt >= 8 ? 11 : 10,
                CompanyId = cnpj ? null : code.Substring(15, 4),
                CnpjBase = cnpj ? code.Substring(15, 8) : null,
                FreeField = cnpj ? code.Substring(23, 21) : code.Substring(19, 25),
            },
        };
    }

    // Returns the indexes of d where a single typo can explain the failures
    // observed, so that SuggestFix only tries plausible fixes:
    //  - one field fails its own check digit: every digit of that field (for
    //    arrecadação, also the value-type digit, which selects the modulus);
    //  - bancário, no field fails: fields 4 and 5, since field 5 has no check
    //    digit of its own and modulo 10 catches any single typo in fields 1-3;
    //  - arrecadação, several blocks fail or the value type is invalid: the
    //    value-type digit, the only single typo that breaks several blocks;
    //  - bancário, several fields fail: more than one typo, no suggestion.
    private static List<int> SearchPositions(string d)
    {
        bool bancario = d.Length == BancarioLineLength;
        ReadOnlySpan<byte> ranges = bancario ? BancarioRanges : ArrecadacaoRanges;
        List<int>? failing = FailingFields(d);
        if (!bancario && (failing is null || failing.Count > 1))
        {
            return new List<int> { 2 };
        }

        // failing is non-null here: bancário lines always have a modulus.
        int failures = failing?.Count ?? 0;
        if (failures == 0)
        {
            return bancario ? Positions(BancarioRanges[6], BancarioRanges[9]) : Positions(0, d.Length);
        }

        if (failures == 1)
        {
            int field = failing![0];
            int lo = ranges[2 * (field - 1)];
            int hi = ranges[(2 * (field - 1)) + 1];
            List<int> positions = Positions(lo, hi);
            if (!bancario && lo > 2)
            {
                // A value-type typo may coincidentally break a single block.
                positions.Insert(0, 2);
            }

            return positions;
        }

        return new List<int>();
    }

    private static List<int> Positions(int lo, int hi)
    {
        var output = new List<int>(hi - lo);
        for (int i = lo; i < hi; i++)
        {
            output.Add(i);
        }

        return output;
    }

    // Returns the 1-based numbers of the fields whose own check digit does
    // not match, or null for an arrecadação line with an invalid value type.
    private static List<int>? FailingFields(string d)
    {
        var failing = new List<int>();
        if (d.Length == BancarioLineLength)
        {
            ReadOnlySpan<byte> fields = BancarioFields;
            for (int i = 0; i < 3; i++)
            {
                int start = fields[2 * i];
                int dvIndex = fields[(2 * i) + 1];
                if (CheckDigits.Mod10Core(d.AsSpan(start, dvIndex - start)) != d[dvIndex])
                {
                    failing.Add(i + 1);
                }
            }

            return failing;
        }

        bool? mod11 = IsMod11(d[2]);
        if (mod11 is null)
        {
            return null;
        }

        ReadOnlySpan<byte> ranges = ArrecadacaoRanges;
        for (int i = 0; i < 4; i++)
        {
            int start = ranges[2 * i];
            int end = ranges[(2 * i) + 1];
            if (ArrecadacaoDigit(d.AsSpan(start, end - start - 1), mod11.Value) != d[end - 1])
            {
                failing.Add(i + 1);
            }
        }

        return failing;
    }
}
