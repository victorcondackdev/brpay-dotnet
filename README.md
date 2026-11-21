# BrPay for .NET

[![CI](https://github.com/victorcondackdev/brpay-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/victorcondackdev/brpay-dotnet/actions/workflows/ci.yml)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Dependencies](https://img.shields.io/badge/dependencies-0-brightgreen)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Zero-dependency .NET library for the payment strings Brazilians paste into
checkout fields every day: **Pix BR Codes** ("copia e cola"), **Pix keys** and
**EndToEndIds**, **boleto** barcodes and linhas digitáveis, and **CPF/CNPJ**,
including the new alphanumeric CNPJ.

It is a port of the Go implementation,
[github.com/victorcondackdev/brpay](https://github.com/victorcondackdev/brpay),
and is **parity-tested against it**: the test suite contains vectors produced by
running the Go code, and the C# port must reproduce them byte for byte,
error messages included.

## Why

- **Zero dependencies, AOT and trimming friendly.** Only the BCL; `IsAotCompatible`
  and `IsTrimmable` are on, and CI publishes a trimmed consumer with warnings as errors.
- **Diagnostics, not just booleans.** EMV errors carry the byte offset and field
  path (`emv: byte 57, field 62.05: length 30 exceeds remaining 12`); boleto
  check-digit failures name the field the user mistyped; the Pix linter reports
  *every* problem in a payload, with the fix.
- **Typo recovery.** `BoletoCode.SuggestFix` proposes the corrected linha digitável
  when a single digit was mistyped.
- **Current rules.** Alphanumeric CNPJ (Receita Federal, July 2026) and the
  boleto due-date factor rollover of 2025-02-22, resolved with the FEBRABAN window.
- **Money is `long` cents.** No `double` anywhere in parsing or building; `decimal Amount`
  is offered only as a display convenience.
- **Exact semantics.** Lengths and offsets are counted in UTF-8 bytes, trimming
  uses Go's white-space set, and formatting is culture-invariant, so both
  implementations agree on every input, including malformed ones.

## Install

```sh
dotnet add package BrPay
```

The package is not on NuGet yet (coming soon). Until then, reference the project
or the `.nupkg` produced by CI (`nuget` artifact of the latest build).

Targets .NET 8 (LTS).

## Quick start

### One entry point for anything pasted

```csharp
using BrPay;

DecodeResult r = Decoder.Decode("00190.00009 00000.500000 00000.000075 9 10000000010000");

r.Kind;          // InputKind.Boleto
r.Success;       // false
r.ErrorMessage;  // "boleto: check digit mismatch in field 2: expected 9, got 0"
r.Suggestions;   // ["00190000090000950000000000000075910000000010000",
                 //  "00190000090000000000000000000075910000000010000"]
```

`Decoder.Decode` never throws for non-null input. `Kind` is one of `Pix`,
`Boleto`, `Document`, `PixKey`, `EndToEndId` or `Unknown`; the matching
property is set on success, and `Error` holds the library exception otherwise.
Pix payloads are always linted (`r.Issues`), even when they fail to decode.

### Pix: decode, build, lint

```csharp
using BrPay.Pix;

BrCode code = BrCode.Decode(payload);          // throws EmvParseException, EmvCrcException or PixException
if (BrCode.TryDecode(payload, out BrCode? c))  // or without exceptions
{
    Console.WriteLine($"{c.KeyType.ToDictName()} {c.Key} {c.Cents} {c.MerchantName}");
}

string copiaECola = BrCodeBuilder.BuildStatic(new StaticBrCodeOptions
{
    Key = "+55 (21) 98765-4321",          // normalized to +5521987654321
    MerchantName = "Padaria Pão Quente",  // transliterated to "Padaria Pao Quente"
    MerchantCity = "Niterói",
    Cents = 1250,
    TxId = "PEDIDO42",
});
// 00020126360014br.gov.bcb.pix0114+5521987654321520400005303986540512.505802BR5918Padaria Pao Quente6007Niteroi62120508PEDIDO4263048559

string dynamic = BrCodeBuilder.BuildDynamic(new DynamicBrCodeOptions
{
    Url = "https://pix.example.com/qr/v2/9d36b84f",
    MerchantName = "Loja",
    MerchantCity = "Rio de Janeiro",
});

foreach (LintIssue issue in Linter.Lint(editedByHand))
{
    Console.WriteLine(issue);
    // warning: payload has surrounding whitespace or line breaks (often a copy-paste artifact); some apps reject it
    // error [63]: CRC is "1D3D", expected 24A3 (was the payload edited after the CRC was computed?)
    // error [60]: non-ASCII or control character 'ó' at byte 5 of the value; transliterate accents ("São" to "Sao")
    // error [54]: amount "10,00": uses a comma; the decimal separator must be a dot
}
```

### Pix keys and EndToEndIds

```csharp
PixKey key = PixKey.Normalize("Fulano@Example.com");   // EMAIL fulano@example.com
PixKeyType type = PixKey.Detect("12.ABC.345/01DE-35"); // PixKeyType.Cnpj

EndToEndId id = EndToEndId.Parse("E60701190202611211430abcdeFGHIJ1");
// id.Ispb == "60701190", id.Timestamp == 2026-11-21 14:30 UTC, id.Sequence == "abcdeFGHIJ1"

string fresh = EndToEndId.Create("60701190", DateTimeOffset.UtcNow); // RandomNumberGenerator, rejection-sampled
```

### Boletos

```csharp
using BrPay.Boleto;

BoletoCode b = BoletoCode.Parse("00190.00009 00000.000000 00000.000075 9 10000000010000");
b.Type;                 // BoletoType.Bancario
b.Barcode;              // 00199100000000100000000000000000000000000007
b.AmountCents;          // 10000
b.Bancario!.BankName;   // Banco do Brasil
b.Bancario.DueDateAt(new DateOnly(2026, 1, 1)); // 2025-02-22 (factor 1000 after the 2025 rollover)

try
{
    BoletoCode.Validate(typedByUser);
}
catch (CheckDigitException e)
{
    // e.Field: 1-3 for the field's own digit, 0 (FieldGeneral) for the DAC
    Console.WriteLine($"Check field {e.Field}: expected {e.Expected}, got {e.Got}");
}

string line = BoletoCode.BarcodeToLine("82650000000100000010000000000000000000000000");
BoletoCode built = BoletoCode.CreateBancario("341", new DateOnly(2026, 12, 10), 15990, "1090100000012345678900001");
int factor = DueDateFactor.FromDate(new DateOnly(2025, 2, 22));  // 1000
HybridBoleto h = HybridBoleto.Parse(barcodeOrLine, pixPayload); // "boleto híbrido" / bolepix
```

### CPF and CNPJ

```csharp
using BrPay.Documents;

Cpf.IsValid("529.982.247-25");          // true
Cnpj.Check("12.ABC.345/01DE-35");       // DocumentError.None (alphanumeric CNPJ)
Cnpj.ComputeCheckDigits("12ABC34501DE"); // (3, 5)
Document.Format("12abc34501de35");      // 12.ABC.345/01DE-35
Document.Mask("52998224725");           // ***.982.247-**
```

### ASP.NET Core minimal API

```csharp
using BrPay;

var app = WebApplication.Create(args);

app.MapPost("/decode", (DecodeRequest r) => Decoder.Decode(r.Input));

app.Run();

public sealed record DecodeRequest(string Input);
```

`DecodeResult` serializes with System.Text.Json out of the box: `Kind` is a
string, the exception is excluded and its message is exposed as `ErrorMessage`.

## Cross-language parity

`tests/BrPay.Tests/Parity/GoVectors.g.cs` is generated by
[`tools/vecgen`](tools/vecgen/main.go), a Go program that runs the reference
implementation over a deterministic corpus: the BCB example payload (CRC `1D3D`),
the CRC-16 check value (`CRC16("123456789") == "29B1"`), the Receita Federal
alphanumeric CNPJ example (`12.ABC.345/01DE-35`), plus hundreds of generated and
mutated inputs (one-character edits, multi-byte characters, truncations) for
every public operation: TLV parsing, CRC verification, BR Code decoding and
linting, static and dynamic generation, key normalization, EndToEndId parsing
and generation from fixed random bytes, boleto building, parsing and typo
suggestions, due-date resolution and the top-level decoder.

`ParityTests` asserts that the C# port produces the same output for each of them,
including error messages. To regenerate, clone both repositories side by side and run:

```sh
go -C tools/vecgen run . > tests/BrPay.Tests/Parity/GoVectors.g.cs
```

A CI job does exactly that and fails if the committed vectors drift.

Known, documented differences:

- When a declared EMV length splits a multi-byte UTF-8 character, Go keeps the
  raw bytes in the value while a .NET `string` holds U+FFFD. Lengths and offsets
  still match (the parser keeps the byte count); only the decoded text differs.
  Such inputs are excluded from the vectors.
- An EndToEndId timestamp in year 0000 parses in Go but not in .NET, where
  `DateTime` starts at year 1.

## Robustness

`RobustnessTests` is the .NET analogue of the Go fuzz targets: 20,000
deterministic random and mutated inputs (including lone surrogates, NUL and
U+2028) go through every entry point. The only exceptions allowed are the
library's own `BrPayException` subclasses, and the same invariants as the Go
fuzzers are checked: TLV round trips, static xor dynamic, cents in range, boleto
representations convert into each other, key normalization is idempotent, and
everything the builder produces decodes back and lints clean.

## Design notes

- **Namespaces mirror the Go packages**: `BrPay.Emv`, `BrPay.Pix`,
  `BrPay.Boleto`, `BrPay.Documents`, and `BrPay.Decoder` at the root.
- **Exceptions and `Try*`.** Every parser has a throwing method and a `Try*`
  counterpart; the exception hierarchy is rooted at `BrPayException`, so one
  `catch` handles any invalid input. `TryDecode`/`TryParse` overloads can also
  return the failure without throwing it.
- **Results are immutable records** (`BrCode`, `BoletoCode`, `DecodeResult`) or
  readonly record structs (`PixKey`, `EndToEndId`, `LintIssue`); dates are
  `DateOnly`, EndToEndId timestamps are UTC `DateTime`.
- **Hot paths avoid allocations**: CRC-16 runs over `ReadOnlySpan<char>` or
  `ReadOnlySpan<byte>` with a constant lookup table; TLV parsing encodes to a
  stack buffer; check digits take spans.
- **No static mutable state**; the clock is injectable through `TimeProvider`.

## Benchmarks

```sh
dotnet run -c Release --project benchmarks/BrPay.Benchmarks -- --filter '*'
```

Covers CRC-16, TLV parsing, `BrCode.Decode`, `Linter.Lint`, `BrCodeBuilder.BuildStatic`,
`BoletoCode.Parse` and `BoletoCode.SuggestFix`.

## Building

```sh
dotnet build -c Release   # warnings are errors
dotnet test -c Release
```

## Disclaimer

This project is not affiliated with, endorsed by or certified by the Banco
Central do Brasil (BCB), FEBRABAN or the Receita Federal. It implements publicly
documented formats; validate against your PSP before going to production.

## License

[MIT](LICENSE) © 2026 Victor Condack
