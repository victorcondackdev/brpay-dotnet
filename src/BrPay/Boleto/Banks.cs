namespace BrPay.Boleto;

/// <summary>A small built-in table of major Brazilian banks by COMPE code.</summary>
public static class Banks
{
    /// <summary>Returns the name of the bank with the given 3-digit COMPE code, or <see langword="null"/> when unknown.</summary>
    /// <param name="code">The COMPE code, e.g. <c>"001"</c>.</param>
    /// <returns>The bank name, or <see langword="null"/>.</returns>
    public static string? GetName(string? code) => code switch
    {
        "001" => "Banco do Brasil",
        "033" => "Santander",
        "041" => "Banrisul",
        "070" => "BRB",
        "077" => "Inter",
        "104" => "Caixa Econômica Federal",
        "212" => "Banco Original",
        "237" => "Bradesco",
        "260" => "Nu Pagamentos",
        "336" => "C6 Bank",
        "341" => "Itaú Unibanco",
        "422" => "Safra",
        "745" => "Citibank",
        "748" => "Sicredi",
        "756" => "Sicoob",
        _ => null,
    };
}
