using System.Text;
using BrPay.Pix;
using BrPay.Tests.Parity;

namespace BrPay.Tests.Pix;

public class EndToEndIdTests
{
    [Fact]
    public void Parse()
    {
        const string s = "E00000000202611211430abcdeFGHIJ1";
        EndToEndId id = EndToEndId.Parse(s);
        Assert.Equal("00000000", id.Ispb);
        Assert.Equal(new DateTime(2026, 11, 21, 14, 30, 0, DateTimeKind.Utc), id.Timestamp);
        Assert.Equal(DateTimeKind.Utc, id.Timestamp.Kind);
        Assert.Equal("abcdeFGHIJ1", id.Sequence);
        Assert.Equal(s, id.ToString());
        Assert.True(EndToEndId.TryParse(s, out EndToEndId again));
        Assert.Equal(id, again);
    }

    [Theory]
    [InlineData("", "length 0")]
    [InlineData("X00000000202611211430abcdeFGHIJ1", "start with \"E\"")]
    [InlineData("E0000000A202611211430abcdeFGHIJ1", "ISPB")]
    [InlineData("E000000002026112114X0abcdeFGHIJ1", "12 digits")]
    [InlineData("E00000000202613211430abcdeFGHIJ1", "not a valid date")]
    [InlineData("E00000000202602301430abcdeFGHIJ1", "not a valid date")]
    [InlineData("E00000000202611211430abcde-GHIJ1", "alphanumeric")]
    public void ParseErrors(string input, string message)
    {
        var e = Assert.Throws<PixException>(() => EndToEndId.Parse(input));
        Assert.Equal(PixError.InvalidEndToEndId, e.Error);
        Assert.Contains(message, e.Message, StringComparison.Ordinal);
        Assert.False(EndToEndId.TryParse(input, out _));
    }

    [Fact]
    public void CreateRoundTrips()
    {
        var at = new DateTimeOffset(2026, 11, 21, 11, 30, 59, TimeSpan.FromHours(-3)); // 14:30 UTC
        string id = EndToEndId.Create("60701190", at);
        EndToEndId parsed = EndToEndId.Parse(id);
        Assert.Equal("60701190", parsed.Ispb);
        Assert.Equal(new DateTime(2026, 11, 21, 14, 30, 0, DateTimeKind.Utc), parsed.Timestamp);
        Assert.NotEqual(id, EndToEndId.Create("60701190", at));
    }

    [Fact]
    public void CreateUsesRejectionSampling()
    {
        // Bytes >= 248 are skipped, the rest map modulo 62 (same vector as the Go tests).
        var bytes = new byte[64];
        new byte[] { 255, 248, 0, 1, 61, 62, 123 }.CopyTo(bytes, 0);
        var at = new DateTimeOffset(2026, 11, 21, 14, 30, 0, TimeSpan.Zero);
        string id = EndToEndId.Create("00000000", at, SequentialRandom.FromBytes(bytes).Fill);
        Assert.Equal("01z0z000000", id.Substring(21));
    }

    // Parity vector: the output of the Go ExampleNewEndToEndID.
    [Fact]
    public void CreateMatchesGoExample()
    {
        byte[] bytes = new byte[64];
        Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("pix-brcode", 4))).CopyTo(bytes, 0);
        var at = new DateTimeOffset(2026, 11, 21, 14, 30, 0, TimeSpan.Zero);
        Assert.Equal("E60701190202611211430ohwjaqbncdo", EndToEndId.Create("60701190", at, SequentialRandom.FromBytes(bytes).Fill));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567A")]
    public void CreateRejectsBadIspb(string ispb)
    {
        var e = Assert.Throws<PixException>(() => EndToEndId.Create(ispb, DateTimeOffset.UnixEpoch));
        Assert.Contains("ISPB", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsUnusableRandomSource()
    {
        Assert.Throws<InvalidOperationException>(() => EndToEndId.Create("00000000", DateTimeOffset.UnixEpoch, buffer => buffer.Fill(0xFF)));
    }
}
