using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
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

/// <summary>
/// Names from the index, plus the place the text names ("Utah", "USA", "Dallas"), so the site's one search box can open a
/// search there as well as a company or person. Postcodes aren't offered here (they go in the location box).
/// </summary>
public sealed class SearchByNameHandler(INameSearchIndex index, IGeoLocator geo) : IRequestHandler<SearchByNameQuery, NameSearchResponse>
{
    public async Task<NameSearchResponse> HandleAsync(SearchByNameQuery q, CancellationToken ct)
    {
        var found = await index.SearchAsync(q.Query, q.Limit ?? SearchByNameQuery.DefaultLimit, q.IncludeExecutives, ct);
        return found with { Places = await PlacesAsync(q.Query.Trim(), ct) };
    }

    private async Task<IReadOnlyList<NameSearchPlaceDto>> PlacesAsync(string text, CancellationToken ct)
    {
        if (Regions.Find(text) is { } region) return [new(region.Name, region.Slug, region.ToDto())];
        if (text.Any(char.IsDigit)) return [];
        var places = new List<NameSearchPlaceDto>();
        if (await geo.LookupAsync(text, ct) is { City.Length: > 0 } city) places.Add(new($"{city.City}, {city.State}", $"{city.City}, {city.State}", null));
        // "Washington" and "New York" are first the cities, but offer the state too.
        if (Regions.StateAlsoNamed(text) is { } state) places.Add(new($"{state.Name} state", state.Slug, state.ToDto()));
        return places;
    }
}
