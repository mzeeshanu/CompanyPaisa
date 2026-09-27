namespace CompanyPaisa.Contracts;

// Money values are in whole units of the company's reporting currency (USD for v1).

/// <summary>
/// The highest-paid chief executive of a company headquartered in the results (latest reported year), and how long they take
/// to earn a typical full-time worker's yearly pay in the company's home country. Null when no such pay is reported.
/// <see cref="Role"/> is "CEO", or "executive director" where the filing doesn't say who the chief executive is (UK annual reports).
/// </summary>
public sealed record TopPaidCeoDto(
    string PersonId,
    string Name,
    string Title,
    string Ticker,
    string CompanyName,
    int Year,
    decimal TotalPay,
    string Currency,
    string Role,
    MedianPayComparisonDto? MedianWorker);

/// <summary>
/// A country's median full-time pay and how many hours (of a 24/7 calendar year) the CEO takes to earn it.
/// <see cref="Approximate"/> = the CEO's pay was converted from another currency first.
/// </summary>
public sealed record MedianPayComparisonDto(
    string Country,
    string Description,
    decimal AnnualPay,
    string Currency,
    string Period,
    string Source,
    string SourceUrl,
    double HoursToEarn,
    bool Approximate);

/// <summary>
/// The company's median employee next to its CEO, as disclosed in its proxy statement (US pay ratio). <see cref="Ratio"/>:
/// the CEO was paid this many times the median employee.
/// </summary>
public sealed record WorkerPayDto(int Year, decimal MedianEmployeePay, decimal CeoPay, decimal Ratio, string Currency, string? SourceFiling);
