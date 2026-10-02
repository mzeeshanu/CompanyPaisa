namespace CompanyPaisa.Core.Domain;

/// <summary>
/// What the company's median employee was paid next to its CEO, as the company itself disclosed it (the US "pay ratio"
/// in the proxy statement). <see cref="Ratio"/> is the stated "N to 1".
/// </summary>
public sealed record WorkerPay
{
    public required string CompanyId { get; init; }
    /// <summary>The fiscal year the figures are for.</summary>
    public required int Year { get; init; }
    public required decimal MedianEmployeePay { get; init; }
    public required decimal CeoPay { get; init; }
    public required decimal Ratio { get; init; }
    public string? SourceFiling { get; init; }
}
