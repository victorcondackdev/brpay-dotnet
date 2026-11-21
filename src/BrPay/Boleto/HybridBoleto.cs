using BrPay.Internal;

namespace BrPay.Boleto;

/// <summary>
/// A "boleto híbrido" (also marketed as "bolepix"): a boleto that also
/// carries a Pix QR code, so the payer can choose either rail.
/// </summary>
public sealed record HybridBoleto
{
    private const string PixPayloadPrefix = "000201";

    /// <summary>The parsed and validated boleto.</summary>
    public required BoletoCode Boleto { get; init; }

    /// <summary>The raw Pix "copia e cola" payload, trimmed of surrounding white space. Decode it with <see cref="BrPay.Pix.BrCode.Decode"/>.</summary>
    public required string PixPayload { get; init; }

    /// <summary>
    /// Parses the boleto (any form accepted by <see cref="BoletoCode.Parse"/>)
    /// and attaches the Pix payload printed next to it. The payload is only
    /// sanity-checked (it must start with the EMV payload format indicator
    /// <c>"000201"</c>); full BR Code validation, including the CRC, is left to the Pix decoder.
    /// </summary>
    /// <param name="barcodeOrLine">The boleto barcode or linha digitável.</param>
    /// <param name="pixPayload">The Pix payload.</param>
    /// <returns>The hybrid boleto.</returns>
    /// <exception cref="BoletoException">The boleto is invalid, or the payload does not look like a BR Code (<see cref="BoletoError.PixPayload"/>).</exception>
    public static HybridBoleto Parse(string barcodeOrLine, string pixPayload)
    {
        ArgumentNullException.ThrowIfNull(barcodeOrLine);
        ArgumentNullException.ThrowIfNull(pixPayload);
        BoletoCode boleto = BoletoCode.Parse(barcodeOrLine);
        string p = AsciiText.TrimSpace(pixPayload);
        if (!p.StartsWith(PixPayloadPrefix, StringComparison.Ordinal))
        {
            throw BoletoException.Of(BoletoError.PixPayload, "missing " + AsciiText.Quote(PixPayloadPrefix) + " prefix");
        }

        return new HybridBoleto { Boleto = boleto, PixPayload = p };
    }
}
