using BrPay.Emv;

namespace BrPay.Tests.Pix;

internal static class TestPayloads
{
    // The static BR Code from the BCB "Manual de Padrões para Iniciação do Pix".
    public const string BcbExample = "00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-426655440000"
        + "5204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***63041D3D";

    /// <summary>Appends the CRC so hand-written vectors stay valid.</summary>
    public static string WithCrc(string body) => Crc16.Append(body);

    /// <summary>Encodes one data object, computing its length.</summary>
    public static string Obj(string id, string value) => id + value.Length.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + value;
}
