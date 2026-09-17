using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Features.Reference;

// ---------- Search by name ----------

/// <remarks>
/// Not recorded in the analytics: the website asks as the visitor types, so every keystroke would count. The company or
/// person they pick is recorded when their page opens.
/// </remarks>
public sealed record SearchByNameQuery(string Query, int? Limit, bool IncludeExecutives) : IRequest<NameSearchResponse>
{
    public const int DefaultLimit = 6;
    public const int MaxLimit = 20;
}

public sealed class SearchByNameValidator : IRequestValidator<SearchByNameQuery>
{
    public IEnumerable<ValidationError> Validate(SearchByNameQuery q)
    {
        if (string.IsNullOrWhiteSpace(q.Query) || q.Query.Trim().Length > 100)
            yield return new("q", "Enter a company name, ticker or person's name (up to 100 characters).");
        if (q.Limit is { } l && (l < 1 || l > SearchByNameQuery.MaxLimit))
            yield return new("limit", $"Limit must be between 1 and {SearchByNameQuery.MaxLimit}.");
    }
}

public sealed class SearchByNameHandler(INameSearchIndex index) : IRequestHandler<SearchByNameQuery, NameSearchResponse>
{
    public Task<NameSearchResponse> HandleAsync(SearchByNameQuery q, CancellationToken ct) =>
        index.SearchAsync(q.Query, q.Limit ?? SearchByNameQuery.DefaultLimit, q.IncludeExecutives, ct);
}
