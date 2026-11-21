using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BrPay.Boleto;
using BrPay.Emv;
using BrPay.Pix;

namespace BrPay.Benchmarks;

/// <summary>Entry point: <c>dotnet run -c Release --project benchmarks/BrPay.Benchmarks -- --filter '*'</c>.</summary>
public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

[MemoryDiagnoser]
public class PixBenchmarks
{
    // The static BR Code from the BCB "Manual de Padrões para Iniciação do Pix".
    private const string Payload = "00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-426655440000"
        + "5204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***63041D3D";

    [Benchmark]
    public ushort Crc16Compute() => Crc16.Compute(Payload.AsSpan());

    [Benchmark]
    public bool Crc16Verify() => Crc16.IsValid(Payload);

    [Benchmark]
    public int TlvParse() => Tlv.Parse(Payload).Count;

    [Benchmark]
    public long BrCodeDecode() => BrCode.Decode(Payload).Cents;

    [Benchmark]
    public int Lint() => Linter.Lint(Payload).Count;

    [Benchmark]
    public string BuildStatic() => BrCodeBuilder.BuildStatic(new StaticBrCodeOptions
    {
        Key = "123e4567-e12b-12d1-a456-426655440000",
        MerchantName = "Fulano de Tal",
        MerchantCity = "BRASILIA",
    });
}

[MemoryDiagnoser]
public class BoletoBenchmarks
{
    private const string Line = "00190.00009 00000.000000 00000.000075 9 10000000010000";
    private const string Typo = "00190.00009 00000.500000 00000.000075 9 10000000010000";

    [Benchmark]
    public long Parse() => BoletoCode.Parse(Line).AmountCents;

    [Benchmark]
    public int SuggestFix() => BoletoCode.SuggestFix(Typo).Count;
}
