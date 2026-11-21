namespace BrPay.Pix;

/// <summary>The reason a Pix operation failed.</summary>
public enum PixError
{
    /// <summary>The Pix key is not a valid CPF, CNPJ, e-mail, phone or EVP.</summary>
    InvalidKey = 1,

    /// <summary>The amount is malformed or out of range.</summary>
    InvalidAmount = 2,

    /// <summary>The merchant name is empty, too long or has unsupported characters.</summary>
    InvalidMerchantName = 3,

    /// <summary>The merchant city is empty, too long or has unsupported characters.</summary>
    InvalidMerchantCity = 4,

    /// <summary>The transaction identifier is too long or not alphanumeric.</summary>
    InvalidTxId = 5,

    /// <summary>The description has unsupported characters or does not fit the template.</summary>
    InvalidDescription = 6,

    /// <summary>The postal code is not 8 digits.</summary>
    InvalidPostalCode = 7,

    /// <summary>The merchant category code is not 4 digits.</summary>
    InvalidMcc = 8,

    /// <summary>The dynamic payload URL is empty, not HTTPS, or does not fit the template.</summary>
    InvalidUrl = 9,

    /// <summary>The payload exceeds 512 bytes.</summary>
    PayloadTooLong = 10,

    /// <summary>The payload has no <c>br.gov.bcb.pix</c> merchant account template.</summary>
    NotPix = 11,

    /// <summary>The merchant account has both a key and a URL.</summary>
    KeyAndUrl = 12,

    /// <summary>The merchant account has neither a key nor a URL.</summary>
    NoKeyOrUrl = 13,

    /// <summary>A required field is missing.</summary>
    MissingField = 14,

    /// <summary>The EndToEndId is malformed.</summary>
    InvalidEndToEndId = 15,
}

/// <summary>Thrown when a Pix payload, key, option or identifier is invalid.</summary>
/// <remarks>Messages are identical to those of the Go implementation, e.g. <c>pix: missing required field: 59 (merchant name)</c>.</remarks>
public sealed class PixException : BrPayException
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="error">The error class.</param>
    /// <param name="message">The complete message.</param>
    public PixException(PixError error, string message)
        : base(message)
    {
        Error = error;
    }

    /// <summary>The error class.</summary>
    public PixError Error { get; }

    /// <summary>Returns the base message of an error class, e.g. <c>pix: invalid key</c>.</summary>
    /// <param name="error">The error class.</param>
    /// <returns>The base message.</returns>
    public static string MessageFor(PixError error) => error switch
    {
        PixError.InvalidKey => "pix: invalid key",
        PixError.InvalidAmount => "pix: invalid amount",
        PixError.InvalidMerchantName => "pix: invalid merchant name",
        PixError.InvalidMerchantCity => "pix: invalid merchant city",
        PixError.InvalidTxId => "pix: invalid txid",
        PixError.InvalidDescription => "pix: invalid description",
        PixError.InvalidPostalCode => "pix: invalid postal code",
        PixError.InvalidMcc => "pix: invalid merchant category code",
        PixError.InvalidUrl => "pix: invalid URL",
        PixError.PayloadTooLong => "pix: payload too long",
        PixError.NotPix => "pix: no br.gov.bcb.pix merchant account template",
        PixError.KeyAndUrl => "pix: merchant account has both a key and a URL",
        PixError.NoKeyOrUrl => "pix: merchant account has neither a key nor a URL",
        PixError.MissingField => "pix: missing required field",
        PixError.InvalidEndToEndId => "pix: invalid EndToEndId",
        _ => "pix: error",
    };

    /// <summary>Creates an exception whose message is the base message followed by <c>": "</c> and a detail.</summary>
    internal static PixException Of(PixError error, string detail) => new(error, MessageFor(error) + ": " + detail);

    /// <summary>Creates an exception with just the base message.</summary>
    internal static PixException Of(PixError error) => new(error, MessageFor(error));
}
