using System.Text;
using BrPay.Emv;

namespace BrPay.Tests.Emv;

public class EmvTests
{
    // The static BR Code published in the Banco Central do Brasil
    // "Manual de Padrões para Iniciação do Pix".
    internal const string BcbExample = "00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-426655440000"
        + "5204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***63041D3D";

    private static string WithoutCrcValue => BcbExample.Substring(0, BcbExample.Length - 4);

    // Independent, table-free CRC-16/CCITT-FALSE used to check the table-driven implementation.
    private static ushort CrcBitwise(byte[] data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
        }

        return crc;
    }

    [Theory]
    [InlineData("123456789", "29B1")] // standard CRC-16/CCITT-FALSE check value
    [InlineData("", "FFFF")]
    public void Crc16CheckValues(string input, string expected) => Assert.Equal(expected, Crc16.ComputeHex(input));

    [Fact]
    public void Crc16OfBcbExampleIs1D3D() => Assert.Equal("1D3D", Crc16.ComputeHex(WithoutCrcValue));

    [Fact]
    public void Crc16MatchesBitwiseReference()
    {
        var samples = new List<byte[]>
        {
            Encoding.UTF8.GetBytes("a"),
            Encoding.UTF8.GetBytes("hello world"),
            Encoding.UTF8.GetBytes(BcbExample),
            Enumerable.Repeat((byte)0xFF, 300).ToArray(),
        };
        foreach (byte[] s in samples)
        {
            Assert.Equal(CrcBitwise(s), Crc16.Compute(s));
        }

        Assert.Equal(Crc16.Compute("123456789"u8), Crc16.Compute("123456789"));
        Assert.Equal(Crc16.Compute(Encoding.UTF8.GetBytes("São Paulo")), Crc16.Compute("São Paulo"));
    }

    [Fact]
    public void AppendAndVerify()
    {
        Assert.Equal(BcbExample, Crc16.Append(BcbExample.Substring(0, BcbExample.Length - 8)));
        Crc16.Verify(BcbExample);
        Assert.True(Crc16.IsValid(BcbExample));
        Assert.True(Crc16.IsValid(WithoutCrcValue + "1d3d"));
    }

    [Theory]
    [InlineData("1D3E")]
    [InlineData("ZZZZ")]
    public void VerifyRejectsWrongCrc(string crc)
    {
        Assert.Throws<EmvCrcException>(() => Crc16.Verify(WithoutCrcValue + crc));
        Assert.False(Crc16.IsValid(WithoutCrcValue + crc));
    }

    [Theory]
    [InlineData("63")]
    [InlineData("")]
    [InlineData("00020126580014br.gov.bcb.pix")]
    public void VerifyRejectsMissingCrcField(string payload)
    {
        var e = Assert.Throws<EmvParseException>(() => Crc16.Verify(payload));
        Assert.Equal(EmvErrorKind.Structure, e.Kind);
        Assert.Equal("63", e.FieldId);
    }

    [Fact]
    public void CrcExceptionDetails()
    {
        var e = Assert.Throws<EmvCrcException>(() => Crc16.Verify(WithoutCrcValue + "0000"));
        Assert.Equal("1D3D", e.Want);
        Assert.Equal("0000", e.Got);
        Assert.Equal("emv: CRC mismatch: payload has \"0000\", computed 1D3D", e.Message);
    }

    [Fact]
    public void ParseBcbExample()
    {
        IReadOnlyList<EmvField> fields = Tlv.Parse(BcbExample);
        Assert.Equal(new[] { "00", "26", "52", "53", "58", "59", "60", "62", "63" }, fields.Select(f => f.Id));

        EmvField? account = Tlv.Find(fields, "26");
        Assert.NotNull(account);
        Assert.Equal(2, account.Children.Count);
        Assert.Equal("123e4567-e12b-12d1-a456-426655440000", account.Child("01")?.Value);

        EmvField? name = Tlv.Find(fields, "59");
        Assert.NotNull(name);
        Assert.Equal("Fulano de Tal", name.Value);
        Assert.Empty(name.Children);
        Assert.Null(Tlv.Find(fields, "54"));

        Tlv.Validate(fields);
        Assert.Equal(BcbExample, Tlv.Encode(fields));
    }

    [Theory]
    [InlineData("000201AB01x", 6, "", "invalid field ID \"AB\": want 2 digits")]
    [InlineData("0002010", 6, "", "truncated field header: need 4 bytes, have 1")]
    [InlineData("00020159XX", 8, "59", "invalid length \"XX\": want 2 digits")]
    [InlineData("00020159300123456789AB", 6, "59", "length 30 exceeds remaining 12")]
    [InlineData("0000", 2, "00", "zero length")]
    [InlineData("000201620505031", 10, "62.05", "length 3 exceeds remaining 1")]
    [InlineData("00020162020X", 10, "62", "truncated field header: need 4 bytes, have 2")]
    public void ParseErrorsArePositional(string input, int offset, string id, string reason)
    {
        var e = Assert.Throws<EmvParseException>(() => Tlv.Parse(input));
        Assert.Equal(offset, e.Offset);
        Assert.Equal(id, e.FieldId);
        Assert.Equal(reason, e.Reason);
        Assert.Equal(EmvErrorKind.Syntax, e.Kind);
        Assert.False(Tlv.TryParse(input, out _));
    }

    [Fact]
    public void ParseErrorMessages()
    {
        var e = Assert.Throws<EmvParseException>(() => Tlv.Parse("00020159300123456789AB"));
        Assert.Equal("emv: byte 6, field 59: length 30 exceeds remaining 12", e.Message);
        Assert.Equal("emv: byte 3: x", new EmvParseException(3, string.Empty, "x", EmvErrorKind.Syntax).Message);
        e = Assert.Throws<EmvParseException>(() => Tlv.Parse("000201593001234567"));
        Assert.Equal("emv: byte 6, field 59: length 30 exceeds remaining 8", e.Message);
    }

    [Fact]
    public void LengthsAreCountedInUtf8Bytes()
    {
        // "Niteró" is 6 characters but 7 bytes, like in the Go implementation.
        IReadOnlyList<EmvField> fields = Tlv.Parse("0002015907Niteró");
        Assert.Equal("Niteró", fields[1].Value);
        Assert.Equal("0002015907Niteró", Tlv.Encode(fields));

        var e = Assert.Throws<EmvParseException>(() => Tlv.Parse("0002015907Niterói6304ABCD"));
        Assert.Equal(17, e.Offset);
        Assert.Equal("invalid field ID \"i6\": want 2 digits", e.Reason);
    }

    [Fact]
    public void ParseTemplate()
    {
        IReadOnlyList<EmvField> got = Tlv.ParseTemplate("0014br.gov.bcb.pix0104abcd");
        Assert.Equal(new[] { new EmvField("00", "br.gov.bcb.pix"), new EmvField("01", "abcd") }, got);

        // Templates are not expanded by ParseTemplate.
        got = Tlv.ParseTemplate("62070503***");
        Assert.Empty(got[0].Children);
        Assert.Throws<EmvParseException>(() => Tlv.ParseTemplate("01"));
    }

    [Theory]
    [InlineData("00", false)]
    [InlineData("25", false)]
    [InlineData("26", true)]
    [InlineData("51", true)]
    [InlineData("52", false)]
    [InlineData("62", true)]
    [InlineData("63", false)]
    [InlineData("64", true)]
    [InlineData("65", false)]
    [InlineData("79", false)]
    [InlineData("80", true)]
    [InlineData("99", true)]
    [InlineData("", false)]
    [InlineData("1", false)]
    [InlineData("ab", false)]
    [InlineData("100", false)]
    public void IsTemplateId(string id, bool expected) => Assert.Equal(expected, Tlv.IsTemplateId(id));

    [Fact]
    public void Encode()
    {
        var fields = new[]
        {
            new EmvField("00", "01"),
            EmvField.Template("26", new EmvField("00", "br.gov.bcb.pix"), new EmvField("01", "k")),
            new EmvField("62", "ignored", new[] { new EmvField("05", "***") }),
            new EmvField("59", new string('x', 99)),
        };
        string expected = "000201" + "26230014br.gov.bcb.pix0101k" + "62070503***" + "5999" + new string('x', 99);
        Assert.Equal(expected, Tlv.Encode(fields));
        Assert.Equal(string.Empty, Tlv.Encode(Array.Empty<EmvField>()));
    }

    [Fact]
    public void EncodeErrors()
    {
        AssertEncodeError("emv: cannot encode field 5: ID must be 2 digits", new EmvField("5", "x"));
        AssertEncodeError("emv: cannot encode field 59: empty value", new EmvField("59", string.Empty));
        AssertEncodeError("emv: cannot encode field 59: value is 100 bytes, max 99", new EmvField("59", new string('x', 100)));
        AssertEncodeError("emv: cannot encode field 62.05: empty value", EmvField.Template("62", new EmvField("05", string.Empty)));
        AssertEncodeError("emv: cannot encode field 26: value is 100 bytes, max 99", EmvField.Template("26", new EmvField("01", new string('x', 96))));

        static void AssertEncodeError(string message, params EmvField[] fields)
        {
            var e = Assert.Throws<EmvEncodeException>(() => Tlv.Encode(fields));
            Assert.Equal(message, e.Message);
        }
    }

    [Fact]
    public void ValidateStructure()
    {
        Tlv.Validate(F("00", "01", "59", "X", "63", "ABCD"));
        AssertStructure(0, "empty payload", Array.Empty<EmvField>());
        AssertStructure(0, "payload format indicator (00) must be the first field", F("59", "X", "00", "01", "63", "ABCD"));
        AssertStructure(0, "payload format indicator must be \"01\", got \"02\"", F("00", "02", "63", "ABCD"));
        AssertStructure(6, "CRC (63) must be the last field", F("00", "01", "63", "ABCD", "59", "X"));
        AssertStructure(6, "CRC (63) must be the last field", F("00", "01", "59", "X"));
        AssertStructure(6, "CRC must be 4 characters, got 3", F("00", "01", "63", "ABC"));
        AssertStructure(11, "duplicate field", F("00", "01", "59", "X", "59", "Y", "63", "ABCD"));
        AssertStructure(6, "invalid field ID", F("00", "01", "x", "X", "63", "ABCD"));
        AssertStructure(17, "CRC (63) must be the last field", new[]
        {
            new EmvField("00", "01"),
            EmvField.Template("62", new EmvField("05", "***")),
            new EmvField("59", "X"),
        });

        static EmvField[] F(params string[] pairs)
        {
            var fields = new EmvField[pairs.Length / 2];
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = new EmvField(pairs[2 * i], pairs[(2 * i) + 1]);
            }

            return fields;
        }

        static void AssertStructure(int offset, string reason, EmvField[] fields)
        {
            var e = Assert.Throws<EmvParseException>(() => Tlv.Validate(fields));
            Assert.Equal(offset, e.Offset);
            Assert.Equal(reason, e.Reason);
            Assert.Equal(EmvErrorKind.Structure, e.Kind);
        }
    }

    [Fact]
    public void FieldEqualityIsStructural()
    {
        var a = EmvField.Template("26", new EmvField("00", "x"));
        var b = new EmvField("26", string.Empty, new[] { new EmvField("00", "x") });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, EmvField.Template("26", new EmvField("00", "y")));
    }
}
