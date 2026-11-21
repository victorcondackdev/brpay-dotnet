namespace BrPay;

/// <summary>
/// Base class of every exception the library throws for invalid input.
/// </summary>
/// <remarks>
/// Catching <see cref="BrPayException"/> is enough to handle any malformed
/// payload, boleto, document, key or identifier. Other exceptions indicate a
/// programming error, such as <see cref="ArgumentNullException"/> for a null
/// argument.
/// </remarks>
public abstract class BrPayException : Exception
{
    /// <summary>Initializes the exception with a message.</summary>
    /// <param name="message">The error message.</param>
    protected BrPayException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and an inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    protected BrPayException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
