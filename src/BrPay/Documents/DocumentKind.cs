namespace BrPay.Documents;

/// <summary>The type of a Brazilian taxpayer identifier.</summary>
public enum DocumentKind
{
    /// <summary>Not a valid CPF or CNPJ.</summary>
    Unknown = 0,

    /// <summary>Cadastro de Pessoas Físicas: 11 digits, identifies individuals.</summary>
    Cpf = 1,

    /// <summary>Cadastro Nacional da Pessoa Jurídica: 14 characters, identifies companies.</summary>
    Cnpj = 2,
}

/// <summary>The reason a CPF or CNPJ failed validation.</summary>
public enum DocumentError
{
    /// <summary>The document is valid.</summary>
    None = 0,

    /// <summary>The cleaned input has the wrong number of characters.</summary>
    Length = 1,

    /// <summary>The input contains a character that is not allowed at its position.</summary>
    Characters = 2,

    /// <summary>Every character is the same, e.g. 111.111.111-11, which passes the check digits but is reserved.</summary>
    Repeated = 3,

    /// <summary>The check digits do not match the base.</summary>
    CheckDigit = 4,
}
