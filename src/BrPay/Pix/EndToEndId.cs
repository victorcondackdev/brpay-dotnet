using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BrPay.Internal;

namespace BrPay.Pix;

/// <summary>Fills a buffer with random bytes. Used to make <see cref="EndToEndId.Create(string, DateTimeOffset, RandomFill)"/> reproducible in tests.</summary>
/// <param name="buffer">The buffer to fill completely.</param>
public delegate void RandomFill(Span<byte> buffer);

/// <summary>
/// A parsed Pix EndToEndId: <c>"E"</c>, the payer PSP's 8-digit ISPB, the
/// creation time as <c>yyyyMMddHHmm</c> in UTC and an 11-character
/// alphanumeric sequence, 32 characters in all.
/// </summary>
public readonly record struct EndToEndId
{
    /// <summary>The length of an EndToEndId.</summary>
    public const int Length = 32;

    private const string TimeFormat = "yyyyMMddHHmm";
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private EndToEndId(string ispb, DateTime timestamp, string sequence)
    {
        Ispb = ispb;
        Timestamp = timestamp;
        Sequence = sequence;
    }

    /// <summary>The 8-digit ISPB of the PSP that created the identifier.</summary>
    public string Ispb { get; }

    /// <summary>The creation time, UTC, minute precision.</summary>
    public DateTime Timestamp { get; }

    /// <summary>The 11 alphanumeric characters that make the identifier unique.</summary>
    public string Sequence { get; }

    /// <summary>Returns the 32-character EndToEndId.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() =>
        "E" + Ispb + Timestamp.ToString(TimeFormat, CultureInfo.InvariantCulture) + Sequence;

    /// <summary>Parses and validates an EndToEndId. The timestamp must be a real calendar date and time.</summary>
    /// <param name="input">The identifier.</param>
    /// <returns>The parsed identifier.</returns>
    /// <exception cref="PixException">The identifier is invalid (<see cref="PixError.InvalidEndToEndId"/>).</exception>
    public static EndToEndId Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        PixException? error = ParseCore(input, out EndToEndId id);
        return error is null ? id : throw error;
    }

    /// <summary>Parses an EndToEndId without throwing.</summary>
    /// <param name="input">The identifier.</param>
    /// <param name="id">The parsed identifier, when valid.</param>
    /// <returns><see langword="true"/> when the identifier is valid.</returns>
    public static bool TryParse(string? input, out EndToEndId id)
    {
        if (input is null)
        {
            id = default;
            return false;
        }

        return ParseCore(input, out id) is null;
    }

    /// <summary>
    /// Creates an EndToEndId for the PSP with the given ISPB at the given
    /// time (converted to UTC, truncated to the minute). The sequence is
    /// drawn uniformly from [0-9A-Za-z] with <see cref="RandomNumberGenerator"/>.
    /// </summary>
    /// <param name="ispb">The 8-digit ISPB.</param>
    /// <param name="at">The creation time.</param>
    /// <returns>The 32-character identifier.</returns>
    /// <exception cref="PixException">The ISPB is not 8 digits.</exception>
    public static string Create(string ispb, DateTimeOffset at) => Create(ispb, at, RandomNumberGenerator.Fill);

    /// <summary>Creates an EndToEndId drawing randomness from <paramref name="random"/>.</summary>
    /// <param name="ispb">The 8-digit ISPB.</param>
    /// <param name="at">The creation time.</param>
    /// <param name="random">The random source; it is asked for 16 bytes at a time.</param>
    /// <returns>The 32-character identifier.</returns>
    /// <exception cref="PixException">The ISPB is not 8 digits.</exception>
    /// <exception cref="InvalidOperationException">The random source keeps returning unusable bytes.</exception>
    public static string Create(string ispb, DateTimeOffset at, RandomFill random)
    {
        ArgumentNullException.ThrowIfNull(ispb);
        ArgumentNullException.ThrowIfNull(random);
        if (ispb.Length != 8 || !AsciiText.IsDigits(ispb))
        {
            throw PixException.Of(PixError.InvalidEndToEndId, "ISPB " + AsciiText.Quote(ispb) + " must be 8 digits");
        }

        DateTime utc = at.UtcDateTime;
        Span<char> output = stackalloc char[Length];
        output[0] = 'E';
        ispb.AsSpan().CopyTo(output.Slice(1));
        string time = utc.ToString(TimeFormat, CultureInfo.InvariantCulture);
        time.AsSpan().CopyTo(output.Slice(9));

        // Rejection sampling keeps the distribution uniform: 248 = 4 * 62.
        // The attempt cap only matters for a broken source that keeps
        // returning bytes >= 248.
        Span<byte> buffer = stackalloc byte[16];
        int n = 21;
        for (int attempts = 0; n < Length; attempts++)
        {
            if (attempts == 64)
            {
                throw new InvalidOperationException("pix: random source yields no usable bytes");
            }

            random(buffer);
            foreach (byte c in buffer)
            {
                if (c < 248 && n < Length)
                {
                    output[n++] = Alphabet[c % 62];
                }
            }
        }

        return output.ToString();
    }

    internal static PixException? ParseCore(string input, out EndToEndId id)
    {
        id = default;
        byte[] s = Encoding.UTF8.GetBytes(input);
        if (s.Length != Length)
        {
            return Invalid("length " + AsciiText.Str(s.Length) + ", want " + AsciiText.Str(Length));
        }

        if (s[0] != 'E')
        {
            return Invalid("must start with \"E\"");
        }

        ReadOnlySpan<byte> ispb = s.AsSpan(1, 8);
        ReadOnlySpan<byte> ts = s.AsSpan(9, 12);
        ReadOnlySpan<byte> seq = s.AsSpan(21);
        if (!AllDigits(ispb))
        {
            return Invalid("ISPB " + AsciiText.QuoteUtf8(ispb) + " must be 8 digits");
        }

        if (!AllDigits(ts))
        {
            return Invalid("timestamp " + AsciiText.QuoteUtf8(ts) + " must be 12 digits");
        }

        int year = Number(ts, 0, 4);
        int month = Number(ts, 4, 2);
        int day = Number(ts, 6, 2);
        int hour = Number(ts, 8, 2);
        int minute = Number(ts, 10, 2);
        if (year < 1 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59)
        {
            return Invalid("timestamp " + AsciiText.QuoteUtf8(ts) + " is not a valid date");
        }

        foreach (byte c in seq)
        {
            if (!AsciiText.IsAlnum((char)c))
            {
                return Invalid("sequence " + AsciiText.QuoteUtf8(seq) + " must be alphanumeric");
            }
        }

        id = new EndToEndId(
            Encoding.ASCII.GetString(ispb),
            new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc),
            Encoding.ASCII.GetString(seq));
        return null;
    }

    private static PixException Invalid(string detail) => PixException.Of(PixError.InvalidEndToEndId, detail);

    private static bool AllDigits(ReadOnlySpan<byte> s)
    {
        foreach (byte c in s)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static int Number(ReadOnlySpan<byte> s, int start, int length)
    {
        int v = 0;
        for (int i = start; i < start + length; i++)
        {
            v = (v * 10) + (s[i] - '0');
        }

        return v;
    }
}
