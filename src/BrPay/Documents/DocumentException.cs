namespace BrPay.Documents;

/// <summary>Thrown when a CPF or CNPJ is invalid.</summary>
public sealed class DocumentException : BrPayException
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="kind">The document type that was being validated.</param>
    /// <param name="error">The reason validation failed; must not be <see cref="DocumentError.None"/>.</param>
    public DocumentException(DocumentKind kind, DocumentError error)
        : base(MessageFor(error))
    {
        Kind = kind;
        Error = error;
    }

    /// <summary>The document type that was being validated.</summary>
    public DocumentKind Kind { get; }

    /// <summary>The reason validation failed.</summary>
    public DocumentError Error { get; }

    /// <summary>
    /// Returns the message used for an error, identical to the Go
    /// implementation (e.g. <c>document: check digit mismatch</c>).
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>The message.</returns>
    public static string MessageFor(DocumentError error) => error switch
    {
        DocumentError.None => "document: valid",
        DocumentError.Length => "document: invalid length",
        DocumentError.Characters => "document: invalid characters",
        DocumentError.Repeated => "document: all digits repeated",
        DocumentError.CheckDigit => "document: check digit mismatch",
        _ => "document: invalid",
    };
}
