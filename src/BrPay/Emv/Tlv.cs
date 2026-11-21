using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using BrPay.Internal;

namespace BrPay.Emv;

/// <summary>
/// The EMVCo Merchant-Presented Mode (MPM) TLV encoding that underlies the Pix BR Code.
/// </summary>
/// <remarks>
/// <para>
/// A payload is a sequence of data objects, each written as a 2-digit ID, a
/// 2-digit decimal length and the value itself. IDs 26–51, 62, 64 and 80–99
/// are templates whose value is itself a TLV list; <see cref="Parse"/>
/// expands them one level deep into <see cref="EmvField.Children"/>.
/// </para>
/// <para>
/// Lengths and offsets are counted in UTF-8 bytes, exactly like the Go
/// implementation. EMV payloads are restricted to printable ASCII, where
/// bytes and characters coincide; counting bytes keeps non-conforming
/// payloads (e.g. an unescaped <c>"São Paulo"</c>) byte-for-byte compatible
/// with what other implementations compute.
/// </para>
/// <para>The API is split by concern:</para>
/// <list type="bullet">
/// <item><description><see cref="Parse"/> and <see cref="ParseTemplate"/> check TLV syntax only.</description></item>
/// <item><description><see cref="Validate"/> checks payload structure (00 = "01" first, 63 last).</description></item>
/// <item><description><see cref="Crc16.Verify"/> checks integrity.</description></item>
/// </list>
/// </remarks>
public static class Tlv
{
    /// <summary>The largest value a single data object can hold, as the length has two decimal digits.</summary>
    public const int MaxValueLength = 99;

    private const int StackLimit = 1024;

    /// <summary>
    /// Reports whether a top-level ID denotes a template: 26–51 (merchant
    /// account information), 62 (additional data), 64 (alternate language)
    /// and 80–99 (unreserved templates).
    /// </summary>
    /// <param name="id">A two-digit ID.</param>
    /// <returns><see langword="true"/> for template IDs.</returns>
    public static bool IsTemplateId(string? id)
    {
        if (!TryId(id, out int n))
        {
            return false;
        }

        return (n >= 26 && n <= 51) || n == 62 || n == 64 || n >= 80;
    }

    /// <summary>Returns the first field with the given ID, or <see langword="null"/>.</summary>
    /// <param name="fields">The fields to search.</param>
    /// <param name="id">The ID to look for.</param>
    /// <returns>The field, or <see langword="null"/>.</returns>
    public static EmvField? Find(IReadOnlyList<EmvField> fields, string id)
    {
        ArgumentNullException.ThrowIfNull(fields);
        foreach (EmvField f in fields)
        {
            if (string.Equals(f.Id, id, StringComparison.Ordinal))
            {
                return f;
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes a payload into its top-level fields, in order, expanding
    /// template fields one level deep. It checks TLV syntax only.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <returns>The fields.</returns>
    /// <exception cref="EmvParseException">The payload is not a valid TLV sequence.</exception>
    public static IReadOnlyList<EmvField> Parse(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return ParseCore(payload, expand: true, out EmvParseException? error) ?? throw error!;
    }

    /// <summary>Decodes a payload like <see cref="Parse"/> without throwing.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="fields">The fields, when successful.</param>
    /// <returns><see langword="true"/> when the payload is valid TLV.</returns>
    public static bool TryParse(string payload, [NotNullWhen(true)] out IReadOnlyList<EmvField>? fields)
    {
        ArgumentNullException.ThrowIfNull(payload);
        fields = ParseCore(payload, expand: true, out _);
        return fields is not null;
    }

    /// <summary>
    /// Decodes the value of a template field into a flat list of fields
    /// (subfields are not expanded). Offsets in errors are relative to <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The template value.</param>
    /// <returns>The subfields.</returns>
    /// <exception cref="EmvParseException">The value is not a valid TLV sequence.</exception>
    public static IReadOnlyList<EmvField> ParseTemplate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ParseCore(value, expand: false, out EmvParseException? error) ?? throw error!;
    }

    /// <summary>
    /// Serializes fields in order, computing every length. Fields with
    /// children are encoded recursively. No CRC is added; see <see cref="Crc16.Append"/>.
    /// </summary>
    /// <param name="fields">The fields to encode.</param>
    /// <returns>The encoded payload.</returns>
    /// <exception cref="EmvEncodeException">An ID is not two digits, or a value is empty or longer than 99 bytes.</exception>
    public static string Encode(IReadOnlyList<EmvField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var b = new StringBuilder();
        EmvEncodeException? error = EncodeTo(b, fields, string.Empty);
        if (error is not null)
        {
            throw error;
        }

        return b.ToString();
    }

    /// <summary>
    /// Checks the structure of a parsed payload: field 00 (payload format
    /// indicator) first with value <c>"01"</c>, field 63 (CRC) last with a
    /// 4-character value, and no repeated top-level ID. Offsets are those
    /// the fields would have in the encoded payload.
    /// </summary>
    /// <param name="fields">The parsed fields.</param>
    /// <exception cref="EmvParseException">The structure is invalid; <see cref="EmvParseException.Kind"/> is <see cref="EmvErrorKind.Structure"/>.</exception>
    public static void Validate(IReadOnlyList<EmvField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        EmvParseException? error = ValidateCore(fields);
        if (error is not null)
        {
            throw error;
        }
    }

    internal static IReadOnlyList<EmvField>? ParseCore(string payload, bool expand, out EmvParseException? error)
    {
        int count = Encoding.UTF8.GetByteCount(payload);
        byte[]? rented = null;
        Span<byte> buffer = count <= StackLimit
            ? stackalloc byte[StackLimit]
            : (rented = ArrayPool<byte>.Shared.Rent(count));
        try
        {
            int written = Encoding.UTF8.GetBytes(payload.AsSpan(), buffer);
            var fields = new List<EmvField>(written / 12 + 1);
            error = ParseBytes(buffer.Slice(0, written), 0, string.Empty, expand, fields);
            return error is null ? fields : null;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static EmvParseException? ParseBytes(ReadOnlySpan<byte> s, int baseOffset, string parent, bool expand, List<EmvField> fields)
    {
        int i = 0;
        while (i < s.Length)
        {
            int rem = s.Length - i;
            if (rem < 4)
            {
                return new EmvParseException(baseOffset + i, parent, "truncated field header: need 4 bytes, have " + AsciiText.Str(rem), EmvErrorKind.Syntax);
            }

            ReadOnlySpan<byte> idBytes = s.Slice(i, 2);
            if (!TryTwoDigits(idBytes, out _))
            {
                return new EmvParseException(baseOffset + i, parent, "invalid field ID " + AsciiText.QuoteUtf8(idBytes) + ": want 2 digits", EmvErrorKind.Syntax);
            }

            string id = Decode(idBytes);
            string path = JoinId(parent, id);
            ReadOnlySpan<byte> lenBytes = s.Slice(i + 2, 2);
            if (!TryTwoDigits(lenBytes, out int n))
            {
                return new EmvParseException(baseOffset + i + 2, path, "invalid length " + AsciiText.QuoteUtf8(lenBytes) + ": want 2 digits", EmvErrorKind.Syntax);
            }

            if (n == 0)
            {
                return new EmvParseException(baseOffset + i + 2, path, "zero length", EmvErrorKind.Syntax);
            }

            int start = i + 4;
            int remaining = s.Length - start;
            if (n > remaining)
            {
                return new EmvParseException(baseOffset + i, path, "length " + AsciiText.Str(n) + " exceeds remaining " + AsciiText.Str(remaining), EmvErrorKind.Syntax);
            }

            ReadOnlySpan<byte> valueBytes = s.Slice(start, n);
            IReadOnlyList<EmvField> children = Array.Empty<EmvField>();
            if (expand && IsTemplateId(id))
            {
                var sub = new List<EmvField>(n / 8 + 1);
                EmvParseException? error = ParseBytes(valueBytes, baseOffset + start, id, expand: false, sub);
                if (error is not null)
                {
                    return error;
                }

                children = sub;
            }

            fields.Add(new EmvField(id, Decode(valueBytes), children, n));
            i = start + n;
        }

        return null;
    }

    private static string Decode(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

    private static EmvEncodeException? EncodeTo(StringBuilder b, IReadOnlyList<EmvField> fields, string prefix)
    {
        foreach (EmvField f in fields)
        {
            if (!TryId(f.Id, out _))
            {
                return new EmvEncodeException(prefix + f.Id, "ID must be 2 digits");
            }

            string v = f.Value;
            if (f.Children.Count > 0)
            {
                var cb = new StringBuilder();
                EmvEncodeException? error = EncodeTo(cb, f.Children, prefix + f.Id + ".");
                if (error is not null)
                {
                    return error;
                }

                v = cb.ToString();
            }

            int len = Encoding.UTF8.GetByteCount(v);
            if (len == 0)
            {
                return new EmvEncodeException(prefix + f.Id, "empty value");
            }

            if (len > MaxValueLength)
            {
                return new EmvEncodeException(prefix + f.Id, "value is " + AsciiText.Str(len) + " bytes, max 99");
            }

            b.Append(f.Id);
            b.Append((char)('0' + (len / 10)));
            b.Append((char)('0' + (len % 10)));
            b.Append(v);
        }

        return null;
    }

    internal static EmvParseException? ValidateCore(IReadOnlyList<EmvField> fields)
    {
        if (fields.Count == 0)
        {
            return new EmvParseException(0, string.Empty, "empty payload", EmvErrorKind.Structure);
        }

        EmvField first = fields[0];
        if (first.Id != "00")
        {
            return new EmvParseException(0, first.Id, "payload format indicator (00) must be the first field", EmvErrorKind.Structure);
        }

        if (first.Value != "01")
        {
            return new EmvParseException(0, "00", "payload format indicator must be \"01\", got " + AsciiText.Quote(first.Value), EmvErrorKind.Structure);
        }

        Span<bool> seen = stackalloc bool[100];
        int off = 0;
        for (int i = 0; i < fields.Count; i++)
        {
            EmvField f = fields[i];
            if (!TryId(f.Id, out int n))
            {
                return new EmvParseException(off, f.Id, "invalid field ID", EmvErrorKind.Structure);
            }

            if (seen[n])
            {
                return new EmvParseException(off, f.Id, "duplicate field", EmvErrorKind.Structure);
            }

            seen[n] = true;
            bool last = i == fields.Count - 1;
            bool isCrc = f.Id == "63";
            if (last && !isCrc)
            {
                return new EmvParseException(off, f.Id, "CRC (63) must be the last field", EmvErrorKind.Structure);
            }

            if (isCrc && !last)
            {
                return new EmvParseException(off, "63", "CRC (63) must be the last field", EmvErrorKind.Structure);
            }

            if (isCrc && f.ByteLength != 4)
            {
                return new EmvParseException(off, "63", "CRC must be 4 characters, got " + AsciiText.Str(f.ByteLength), EmvErrorKind.Structure);
            }

            off += 4 + ValueLength(f);
        }

        return null;
    }

    /// <summary>The encoded length, in bytes, of a field's value.</summary>
    internal static int ValueLength(EmvField f)
    {
        if (f.Children.Count == 0)
        {
            return f.ByteLength;
        }

        int n = 0;
        foreach (EmvField c in f.Children)
        {
            n += 4 + ValueLength(c);
        }

        return n;
    }

    private static string JoinId(string parent, string id) => parent.Length == 0 ? id : parent + "." + id;

    /// <summary>Parses exactly two ASCII digits.</summary>
    internal static bool TryId(string? s, out int value)
    {
        if (s is null || s.Length != 2 || !AsciiText.IsDigit(s[0]) || !AsciiText.IsDigit(s[1]))
        {
            value = 0;
            return false;
        }

        value = ((s[0] - '0') * 10) + (s[1] - '0');
        return true;
    }

    private static bool TryTwoDigits(ReadOnlySpan<byte> s, out int value)
    {
        if (s.Length != 2 || s[0] < (byte)'0' || s[0] > (byte)'9' || s[1] < (byte)'0' || s[1] > (byte)'9')
        {
            value = 0;
            return false;
        }

        value = ((s[0] - '0') * 10) + (s[1] - '0');
        return true;
    }
}
