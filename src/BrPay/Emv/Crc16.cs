using System.Buffers;
using System.Text;

namespace BrPay.Emv;

/// <summary>
/// CRC-16/CCITT-FALSE (polynomial 0x1021, initial value 0xFFFF, no
/// reflection, no final XOR), the checksum stored in field 63 of every BR Code.
/// </summary>
/// <remarks>
/// Text is checksummed as its UTF-8 bytes, which is what every other
/// implementation hashes. ASCII input, the only kind a conforming payload
/// contains, takes an allocation-free fast path.
/// </remarks>
public static class Crc16
{
    /// <summary>The ID and length of the CRC field (<c>"63"</c>, <c>"04"</c>) that precede the checksum. The checksum covers it.</summary>
    public const string FieldPrefix = "6304";

    private const int StackLimit = 1024;

    private const string HexDigits = "0123456789ABCDEF";

    /// <summary>Computes the checksum of raw bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The CRC.</returns>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<ushort> table = Table;
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc = (ushort)((crc << 8) ^ table[(crc >> 8) ^ b]);
        }

        return crc;
    }

    /// <summary>Computes the checksum of text, encoded as UTF-8.</summary>
    /// <param name="data">The text.</param>
    /// <returns>The CRC.</returns>
    public static ushort Compute(ReadOnlySpan<char> data)
    {
        ReadOnlySpan<ushort> table = Table;
        ushort crc = 0xFFFF;
        foreach (char c in data)
        {
            if (c > 0x7F)
            {
                return ComputeUtf8(data);
            }

            crc = (ushort)((crc << 8) ^ table[(crc >> 8) ^ c]);
        }

        return crc;
    }

    /// <summary>Computes the checksum of text as 4 uppercase hexadecimal digits, the representation used in field 63.</summary>
    /// <param name="data">The text.</param>
    /// <returns>The CRC, e.g. <c>"1D3D"</c>.</returns>
    public static string ComputeHex(ReadOnlySpan<char> data) => ToHex(Compute(data));

    /// <summary>Formats a checksum as 4 uppercase hexadecimal digits.</summary>
    /// <param name="crc">The checksum.</param>
    /// <returns>The hexadecimal representation.</returns>
    public static string ToHex(ushort crc) => new string(new[]
    {
        HexDigits[crc >> 12],
        HexDigits[(crc >> 8) & 0xF],
        HexDigits[(crc >> 4) & 0xF],
        HexDigits[crc & 0xF],
    });

    /// <summary>
    /// Appends the CRC field to a payload that does not have one yet: it adds
    /// <c>"6304"</c> and the checksum computed over everything including
    /// <c>"6304"</c>. The input is not inspected.
    /// </summary>
    /// <param name="payload">The payload without field 63.</param>
    /// <returns>The payload with its CRC field.</returns>
    public static string Append(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string p = payload + FieldPrefix;
        return p + ComputeHex(p);
    }

    /// <summary>
    /// Checks that a payload ends with a CRC field (<c>"6304"</c> plus 4 hex
    /// digits) whose value matches the checksum of everything before those
    /// 4 digits. Hex digits are compared case-insensitively.
    /// </summary>
    /// <param name="payload">The full payload.</param>
    /// <exception cref="EmvParseException">The payload does not end with a CRC field.</exception>
    /// <exception cref="EmvCrcException">The checksum does not match.</exception>
    public static void Verify(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        EmvException? error = VerifyCore(payload);
        if (error is not null)
        {
            throw error;
        }
    }

    /// <summary>Reports whether a payload ends with a matching CRC field.</summary>
    /// <param name="payload">The full payload.</param>
    /// <returns><see langword="true"/> when the CRC is present and correct.</returns>
    public static bool IsValid(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return VerifyCore(payload) is null;
    }

    internal static EmvException? VerifyCore(string payload)
    {
        int count = Encoding.UTF8.GetByteCount(payload);
        byte[]? rented = null;
        Span<byte> buffer = count <= StackLimit
            ? stackalloc byte[StackLimit]
            : (rented = ArrayPool<byte>.Shared.Rent(count));
        try
        {
            int n = Encoding.UTF8.GetBytes(payload.AsSpan(), buffer);
            return VerifyBytes(buffer.Slice(0, n));
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    internal static EmvException? VerifyBytes(ReadOnlySpan<byte> p)
    {
        int n = p.Length;
        if (n < 8 || !p.Slice(n - 8, 4).SequenceEqual("6304"u8))
        {
            return new EmvParseException(Math.Max(0, n - 8), "63", "payload does not end with a CRC field (6304XXXX)", EmvErrorKind.Structure);
        }

        ReadOnlySpan<byte> got = p.Slice(n - 4);
        ushort want = Compute(p.Slice(0, n - 4));
        if (!TryParseHex(got, out ushort value) || value != want)
        {
            return new EmvCrcException(Encoding.UTF8.GetString(got), ToHex(want));
        }

        return null;
    }

    private static ushort ComputeUtf8(ReadOnlySpan<char> data)
    {
        int count = Encoding.UTF8.GetByteCount(data);
        byte[]? rented = null;
        Span<byte> buffer = count <= StackLimit
            ? stackalloc byte[StackLimit]
            : (rented = ArrayPool<byte>.Shared.Rent(count));
        try
        {
            int n = Encoding.UTF8.GetBytes(data, buffer);
            return Compute((ReadOnlySpan<byte>)buffer.Slice(0, n));
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static bool TryParseHex(ReadOnlySpan<byte> s, out ushort value)
    {
        int v = 0;
        foreach (byte c in s)
        {
            int d;
            if (c >= '0' && c <= '9')
            {
                d = c - '0';
            }
            else if (c >= 'A' && c <= 'F')
            {
                d = c - 'A' + 10;
            }
            else if (c >= 'a' && c <= 'f')
            {
                d = c - 'a' + 10;
            }
            else
            {
                value = 0;
                return false;
            }

            v = (v << 4) | d;
        }

        value = (ushort)v;
        return true;
    }

    // Byte-wise lookup table for CRC-16/CCITT-FALSE, generated from the Go
    // implementation. The compiler emits it as constant data (no allocation).
    private static ReadOnlySpan<ushort> Table => new ushort[]
    {
        0x0000, 0x1021, 0x2042, 0x3063, 0x4084, 0x50A5, 0x60C6, 0x70E7,
        0x8108, 0x9129, 0xA14A, 0xB16B, 0xC18C, 0xD1AD, 0xE1CE, 0xF1EF,
        0x1231, 0x0210, 0x3273, 0x2252, 0x52B5, 0x4294, 0x72F7, 0x62D6,
        0x9339, 0x8318, 0xB37B, 0xA35A, 0xD3BD, 0xC39C, 0xF3FF, 0xE3DE,
        0x2462, 0x3443, 0x0420, 0x1401, 0x64E6, 0x74C7, 0x44A4, 0x5485,
        0xA56A, 0xB54B, 0x8528, 0x9509, 0xE5EE, 0xF5CF, 0xC5AC, 0xD58D,
        0x3653, 0x2672, 0x1611, 0x0630, 0x76D7, 0x66F6, 0x5695, 0x46B4,
        0xB75B, 0xA77A, 0x9719, 0x8738, 0xF7DF, 0xE7FE, 0xD79D, 0xC7BC,
        0x48C4, 0x58E5, 0x6886, 0x78A7, 0x0840, 0x1861, 0x2802, 0x3823,
        0xC9CC, 0xD9ED, 0xE98E, 0xF9AF, 0x8948, 0x9969, 0xA90A, 0xB92B,
        0x5AF5, 0x4AD4, 0x7AB7, 0x6A96, 0x1A71, 0x0A50, 0x3A33, 0x2A12,
        0xDBFD, 0xCBDC, 0xFBBF, 0xEB9E, 0x9B79, 0x8B58, 0xBB3B, 0xAB1A,
        0x6CA6, 0x7C87, 0x4CE4, 0x5CC5, 0x2C22, 0x3C03, 0x0C60, 0x1C41,
        0xEDAE, 0xFD8F, 0xCDEC, 0xDDCD, 0xAD2A, 0xBD0B, 0x8D68, 0x9D49,
        0x7E97, 0x6EB6, 0x5ED5, 0x4EF4, 0x3E13, 0x2E32, 0x1E51, 0x0E70,
        0xFF9F, 0xEFBE, 0xDFDD, 0xCFFC, 0xBF1B, 0xAF3A, 0x9F59, 0x8F78,
        0x9188, 0x81A9, 0xB1CA, 0xA1EB, 0xD10C, 0xC12D, 0xF14E, 0xE16F,
        0x1080, 0x00A1, 0x30C2, 0x20E3, 0x5004, 0x4025, 0x7046, 0x6067,
        0x83B9, 0x9398, 0xA3FB, 0xB3DA, 0xC33D, 0xD31C, 0xE37F, 0xF35E,
        0x02B1, 0x1290, 0x22F3, 0x32D2, 0x4235, 0x5214, 0x6277, 0x7256,
        0xB5EA, 0xA5CB, 0x95A8, 0x8589, 0xF56E, 0xE54F, 0xD52C, 0xC50D,
        0x34E2, 0x24C3, 0x14A0, 0x0481, 0x7466, 0x6447, 0x5424, 0x4405,
        0xA7DB, 0xB7FA, 0x8799, 0x97B8, 0xE75F, 0xF77E, 0xC71D, 0xD73C,
        0x26D3, 0x36F2, 0x0691, 0x16B0, 0x6657, 0x7676, 0x4615, 0x5634,
        0xD94C, 0xC96D, 0xF90E, 0xE92F, 0x99C8, 0x89E9, 0xB98A, 0xA9AB,
        0x5844, 0x4865, 0x7806, 0x6827, 0x18C0, 0x08E1, 0x3882, 0x28A3,
        0xCB7D, 0xDB5C, 0xEB3F, 0xFB1E, 0x8BF9, 0x9BD8, 0xABBB, 0xBB9A,
        0x4A75, 0x5A54, 0x6A37, 0x7A16, 0x0AF1, 0x1AD0, 0x2AB3, 0x3A92,
        0xFD2E, 0xED0F, 0xDD6C, 0xCD4D, 0xBDAA, 0xAD8B, 0x9DE8, 0x8DC9,
        0x7C26, 0x6C07, 0x5C64, 0x4C45, 0x3CA2, 0x2C83, 0x1CE0, 0x0CC1,
        0xEF1F, 0xFF3E, 0xCF5D, 0xDF7C, 0xAF9B, 0xBFBA, 0x8FD9, 0x9FF8,
        0x6E17, 0x7E36, 0x4E55, 0x5E74, 0x2E93, 0x3EB2, 0x0ED1, 0x1EF0,
    };
}
