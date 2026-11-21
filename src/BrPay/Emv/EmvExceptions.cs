using BrPay.Internal;

namespace BrPay.Emv;

/// <summary>The class of an <see cref="EmvParseException"/>.</summary>
public enum EmvErrorKind
{
    /// <summary>The input is not a valid TLV sequence (bad ID, bad length, truncated value).</summary>
    Syntax = 1,

    /// <summary>The TLV is well formed but the payload layout is invalid (e.g. field 00 not first, 63 not last).</summary>
    Structure = 2,
}

/// <summary>Base class of the exceptions thrown by the EMV layer.</summary>
public abstract class EmvException : BrPayException
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">The error message.</param>
    protected EmvException(string message)
        : base(message)
    {
    }
}

/// <summary>Describes where and why decoding a payload failed.</summary>
/// <remarks>
/// The message has the form <c>emv: byte 57, field 59: length 30 exceeds remaining 12</c>,
/// identical to the Go implementation, which makes it suitable for diagnostics and linters.
/// </remarks>
public sealed class EmvParseException : EmvException
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="offset">The UTF-8 byte offset where the problem was found.</param>
    /// <param name="fieldId">The field ID involved, as a dotted path for subfields, or empty.</param>
    /// <param name="reason">A human-readable description of the problem.</param>
    /// <param name="kind">The error class.</param>
    public EmvParseException(int offset, string fieldId, string reason, EmvErrorKind kind)
        : base(Format(offset, fieldId, reason))
    {
        Offset = offset;
        FieldId = fieldId;
        Reason = reason;
        Kind = kind;
    }

    /// <summary>The UTF-8 byte offset in the input where the problem was found.</summary>
    public int Offset { get; }

    /// <summary>
    /// The field ID involved, as a dotted path for nested fields (e.g. <c>62.05</c>).
    /// Empty when the ID itself could not be read.
    /// </summary>
    public string FieldId { get; }

    /// <summary>A human-readable description of the problem.</summary>
    public string Reason { get; }

    /// <summary>The error class: <see cref="EmvErrorKind.Syntax"/> or <see cref="EmvErrorKind.Structure"/>.</summary>
    public EmvErrorKind Kind { get; }

    private static string Format(int offset, string fieldId, string reason)
    {
        string s = "emv: byte " + AsciiText.Str(offset);
        if (fieldId.Length > 0)
        {
            s += ", field " + fieldId;
        }

        return s + ": " + reason;
    }
}

/// <summary>Reports a checksum mismatch in field 63.</summary>
public sealed class EmvCrcException : EmvException
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="got">The CRC found in the payload, as written.</param>
    /// <param name="want">The CRC computed over the payload.</param>
    public EmvCrcException(string got, string want)
        : base("emv: CRC mismatch: payload has " + AsciiText.Quote(got) + ", computed " + want)
    {
        Got = got;
        Want = want;
    }

    /// <summary>The CRC found in the payload, as written.</summary>
    public string Got { get; }

    /// <summary>The CRC computed over the payload (4 uppercase hex digits).</summary>
    public string Want { get; }
}

/// <summary>Thrown by <see cref="Tlv.Encode"/> when a field cannot be represented.</summary>
public sealed class EmvEncodeException : EmvException
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="fieldId">The dotted path of the field.</param>
    /// <param name="reason">Why it cannot be encoded.</param>
    public EmvEncodeException(string fieldId, string reason)
        : base("emv: cannot encode field " + fieldId + ": " + reason)
    {
        FieldId = fieldId;
        Reason = reason;
    }

    /// <summary>The dotted path of the field, e.g. <c>62.05</c>.</summary>
    public string FieldId { get; }

    /// <summary>Why the field cannot be encoded.</summary>
    public string Reason { get; }
}
