namespace CompanyPaisa.Core.Domain;

/// <summary>
/// What a company pays for one job title, company-wide when <see cref="City"/> is null, else at one work place. From one
/// of two sources (<see cref="Source"/>): its H-1B wage filings (US Department of Labor; the salary committed, only titles
/// with enough filings to not single anyone out), or its job ads (the pay range US pay-transparency laws require).
/// </summary>
public sealed record JobSalary
{
    /// <summary><see cref="Source"/> values.</summary>
    public const string VisaFilings = "h1b", JobAds = "postings";

    public required string CompanyId { get; init; }
    public string Source { get; init; } = VisaFilings;
    /// <summary>A current job ad for this title (job ads only).</summary>
    public string? Url { get; init; }
    public required string Title { get; init; }
    /// <summary>The government's occupation name for the job ("Software Developers").</summary>
    public string? Occupation { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    /// <summary>The work place's ZIP code centre (place rows only).</summary>
    public GeoPoint? Point { get; init; }
    /// <summary>How many filings (visa filings) or ads (job ads) the figures come from.</summary>
    public required int Filings { get; init; }
    /// <summary>
    /// Visa filings: 25th percentile, median and 75th percentile of the yearly salary offered. Job ads: the typical bottom
    /// of the advertised range, the typical middle and the typical top; Min and Max are the lowest bottom and highest top.
    /// </summary>
    public required decimal Low { get; init; }
    public required decimal Median { get; init; }
    public required decimal High { get; init; }
    public required decimal Min { get; init; }
    public required decimal Max { get; init; }
}

/// <summary>Where one kind of job salaries comes from (<see cref="JobSalary.Source"/>) and the dates of the filings or ads.</summary>
public sealed record JobSalarySource(DateOnly From, DateOnly To, string Source, string Kind = JobSalary.VisaFilings);
