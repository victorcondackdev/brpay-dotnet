namespace BrPay.Boleto;

/// <summary>The reason a boleto operation failed.</summary>
public enum BoletoError
{
    /// <summary>The digit count is not 44, 47 or 48 (or the length a conversion expects).</summary>
    Length = 1,

    /// <summary>A character other than digits and the accepted separators (space, tab, newline, <c>.</c>, <c>-</c>).</summary>
    Characters = 2,

    /// <summary>A check digit does not match; the exception is a <see cref="CheckDigitException"/>.</summary>
    CheckDigit = 3,

    /// <summary>An arrecadação code that does not start with 8, or a bancário code that does.</summary>
    ProductId = 4,

    /// <summary>An arrecadação value-type identifier (third digit) other than 6, 7, 8 or 9.</summary>
    ValueType = 5,

    /// <summary>A bank code that is not three digits.</summary>
    BankCode = 6,

    /// <summary>An amount that is negative or does not fit the barcode's amount field.</summary>
    Amount = 7,

    /// <summary>A free field of the wrong size or with non-digits.</summary>
    FreeField = 8,

    /// <summary>An unknown arrecadação segment.</summary>
    Segment = 9,

    /// <summary>A due date that cannot be encoded as a factor.</summary>
    DueDate = 10,

    /// <summary>A Pix payload that does not look like a BR Code.</summary>
    PixPayload = 11,
}

/// <summary>Thrown when a boleto barcode, linha digitável or construction parameter is invalid.</summary>
/// <remarks>Messages are identical to the Go implementation, e.g. <c>boleto: invalid length: got 3 digits, want 44, 47 or 48</c>.</remarks>
public class BoletoException : BrPayException
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="error">The error class.</param>
    /// <param name="message">The complete message.</param>
    public BoletoException(BoletoError error, string message)
        : base(message)
    {
        Error = error;
    }

    /// <summary>The error class.</summary>
    public BoletoError Error { get; }

    /// <summary>Returns the base message of an error class, e.g. <c>boleto: invalid length</c>.</summary>
    /// <param name="error">The error class.</param>
    /// <returns>The base message.</returns>
    public static string MessageFor(BoletoError error) => error switch
    {
        BoletoError.Length => "boleto: invalid length",
        BoletoError.Characters => "boleto: invalid character",
        BoletoError.CheckDigit => "boleto: check digit mismatch",
        BoletoError.ProductId => "boleto: invalid product identifier",
        BoletoError.ValueType => "boleto: invalid value type identifier",
        BoletoError.BankCode => "boleto: invalid bank code",
        BoletoError.Amount => "boleto: amount out of range",
        BoletoError.FreeField => "boleto: invalid free field",
        BoletoError.Segment => "boleto: invalid segment",
        BoletoError.DueDate => "boleto: due date out of range",
        BoletoError.PixPayload => "boleto: invalid Pix payload",
        _ => "boleto: error",
    };

    internal static BoletoException Of(BoletoError error, string detail) => new(error, MessageFor(error) + ": " + detail);
}

/// <summary>
/// A check digit that does not match its data, naming the field the user
/// most likely mistyped so an interface can point at it.
/// </summary>
public sealed class CheckDigitException : BoletoException
{
    /// <summary>The <see cref="Field"/> value used for the general check digit (DAC) of the whole code.</summary>
    public const int FieldGeneral = 0;

    /// <summary>Initializes the exception.</summary>
    /// <param name="field">The 1-based field or block, or <see cref="FieldGeneral"/>.</param>
    /// <param name="expected">The computed check digit.</param>
    /// <param name="got">The check digit found in the input.</param>
    public CheckDigitException(int field, char expected, char got)
        : base(BoletoError.CheckDigit, FormatMessage(field, expected, got))
    {
        Field = field;
        Expected = expected;
        Got = got;
    }

    /// <summary>
    /// The 1-based field (bancário) or block (arrecadação) of the linha
    /// digitável whose own check digit failed, or <see cref="FieldGeneral"/>
    /// for the general check digit. In a bancário linha digitável the general
    /// check digit is printed as field 4; in an arrecadação code it is the fourth digit.
    /// </summary>
    public int Field { get; }

    /// <summary>The computed check digit (<c>'0'</c>–<c>'9'</c>).</summary>
    public char Expected { get; }

    /// <summary>The check digit found in the input (<c>'0'</c>–<c>'9'</c>).</summary>
    public char Got { get; }

    private static string FormatMessage(int field, char expected, char got) =>
        field == FieldGeneral
            ? "boleto: general check digit mismatch: expected " + expected + ", got " + got
            : "boleto: check digit mismatch in field " + field.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": expected " + expected + ", got " + got;
}
