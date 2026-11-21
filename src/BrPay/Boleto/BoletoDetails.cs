namespace BrPay.Boleto;

/// <summary>The boleto family.</summary>
public enum BoletoType
{
    /// <summary>Not determined.</summary>
    Unknown = 0,

    /// <summary>Bank slip ("boleto de cobrança"): 44-digit barcode, 47-digit linha digitável.</summary>
    Bancario = 1,

    /// <summary>Utility bill, tax or fine ("convênio"): barcode starting with 8, 48-digit linha digitável.</summary>
    Arrecadacao = 2,
}

/// <summary>The arrecadação segment identifier (second barcode digit), as defined by FEBRABAN.</summary>
/// <remarks>Parsed codes may carry undefined values (0 or 8); <see cref="BoletoNames.GetDescription(ArrecadacaoSegment)"/> returns <see langword="null"/> for them.</remarks>
public enum ArrecadacaoSegment
{
    /// <summary>City halls ("Prefeituras").</summary>
    Prefeituras = 1,

    /// <summary>Water and sanitation ("Saneamento").</summary>
    Saneamento = 2,

    /// <summary>Electricity and gas ("Energia elétrica e gás").</summary>
    EnergiaGas = 3,

    /// <summary>Telecommunications ("Telecomunicações").</summary>
    Telecom = 4,

    /// <summary>Government agencies ("Órgãos governamentais").</summary>
    Governo = 5,

    /// <summary>Carnês and other companies identified by CNPJ.</summary>
    Cnpj = 6,

    /// <summary>Traffic fines ("Multas de trânsito").</summary>
    MultasTransito = 7,

    /// <summary>Reserved for the bank ("Uso exclusivo do banco").</summary>
    Banco = 9,
}

/// <summary>Portuguese display names for boleto enums.</summary>
public static class BoletoNames
{
    /// <summary>Returns the Portuguese name of the family: <c>bancário</c>, <c>arrecadação</c> or <c>unknown</c>.</summary>
    /// <param name="type">The family.</param>
    /// <returns>The name.</returns>
    public static string GetDisplayName(this BoletoType type) => type switch
    {
        BoletoType.Bancario => "bancário",
        BoletoType.Arrecadacao => "arrecadação",
        _ => "unknown",
    };

    /// <summary>Returns the FEBRABAN (Portuguese) name of a segment, or <see langword="null"/> when it is not defined.</summary>
    /// <param name="segment">The segment.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public static string? GetDescription(this ArrecadacaoSegment segment) => segment switch
    {
        ArrecadacaoSegment.Prefeituras => "Prefeituras",
        ArrecadacaoSegment.Saneamento => "Saneamento",
        ArrecadacaoSegment.EnergiaGas => "Energia elétrica e gás",
        ArrecadacaoSegment.Telecom => "Telecomunicações",
        ArrecadacaoSegment.Governo => "Órgãos governamentais",
        ArrecadacaoSegment.Cnpj => "Carnês e demais empresas identificadas por CNPJ",
        ArrecadacaoSegment.MultasTransito => "Multas de trânsito",
        ArrecadacaoSegment.Banco => "Uso exclusivo do banco",
        _ => null,
    };
}

/// <summary>The fields specific to a bank slip.</summary>
public sealed record BancarioDetails
{
    /// <summary>The 3-digit COMPE code of the issuing bank.</summary>
    public string BankCode { get; init; } = string.Empty;

    /// <summary>The bank name from a small built-in table, or <see langword="null"/>.</summary>
    public string? BankName { get; init; }

    /// <summary>The currency code; 9 means Brazilian real.</summary>
    public int Currency { get; init; }

    /// <summary>The due-date factor; 0 means no due date.</summary>
    public int Factor { get; init; }

    /// <summary>The 25-digit bank-specific "campo livre".</summary>
    public string FreeField { get; init; } = string.Empty;

    /// <summary>Resolves the due date relative to a reference date; see <see cref="DueDateFactor.ToDate"/>.</summary>
    /// <param name="reference">The reference date, usually today.</param>
    /// <returns>The due date, or <see langword="null"/> when the boleto has no due date (factor 0000).</returns>
    public DateOnly? DueDateAt(DateOnly reference) => DueDateFactor.ToDate(Factor, reference);

    /// <summary>Resolves the due date relative to the current local date.</summary>
    /// <param name="timeProvider">The clock; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    /// <returns>The due date, or <see langword="null"/> when the boleto has no due date.</returns>
    public DateOnly? DueDate(TimeProvider? timeProvider = null) =>
        DueDateAt(DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetLocalNow().DateTime));
}

/// <summary>The fields specific to utility bills, taxes and fines.</summary>
public sealed record ArrecadacaoDetails
{
    /// <summary>The segment identifier.</summary>
    public ArrecadacaoSegment Segment { get; init; }

    /// <summary>The value-type identifier (6, 7, 8 or 9).</summary>
    public int ValueType { get; init; }

    /// <summary><see langword="true"/> for value types 7 and 9, where the amount field is a reference (e.g. a quantity or index) rather than money.</summary>
    public bool ValueIsReference { get; init; }

    /// <summary>10 (value types 6, 7) or 11 (value types 8, 9).</summary>
    public int Modulus { get; init; }

    /// <summary>The 4-digit company or agency code; <see langword="null"/> for <see cref="ArrecadacaoSegment.Cnpj"/>.</summary>
    public string? CompanyId { get; init; }

    /// <summary>The 8-digit CNPJ base; only set for <see cref="ArrecadacaoSegment.Cnpj"/>.</summary>
    public string? CnpjBase { get; init; }

    /// <summary>The remainder of the barcode after the identification.</summary>
    public string FreeField { get; init; } = string.Empty;
}
