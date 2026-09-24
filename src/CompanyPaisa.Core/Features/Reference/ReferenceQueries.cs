using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Mapping;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Features.Reference;

// ---------- ZIP / city lookup ----------

/// <remarks>Recorded as "place_lookup": the postcode or city typed and where it resolved to (no coordinates finer than the postcode).</remarks>
public sealed record LookupGeoQuery(string Query) : IRequest<GeoLookupDto>, ITrackedRequest<GeoLookupDto>
{
    public AnalyticsAction Describe(GeoLookupDto response) =>
        new("place_lookup", Query.Trim().ToUpperInvariant(), string.Join(", ", new[] { response.City, response.State }.Where(s => !string.IsNullOrWhiteSpace(s))));
}

public sealed class LookupGeoValidator : IRequestValidator<LookupGeoQuery>
{
    public IEnumerable<ValidationError> Validate(LookupGeoQuery q)
    {
        if (string.IsNullOrWhiteSpace(q.Query) || q.Query.Length > 100)
            yield return new("query", "Enter a ZIP code or postcode, a city, or a country, state or province.");
    }
}

/// <summary>
/// A country, state or province name ("Texas", "UK", "Ontario") resolves to that <see cref="Region"/>, centred on its companies;
/// anything else (ZIP, postcode, city) to a point from the local postcode tables.
/// </summary>
public sealed class LookupGeoHandler(IGeoLocator geoLocator, INearbySearchService nearby) : IRequestHandler<LookupGeoQuery, GeoLookupDto>
{
    public async Task<GeoLookupDto> HandleAsync(LookupGeoQuery q, CancellationToken ct)
    {
        if (Regions.Find(q.Query) is { } region)
        {
            var hits = await nearby.FindCompaniesInRegionAsync(region, ct);
            var centre = NearbySearchService.Centre(hits.Values.Select(h => h.NearestLocation.Point));
            return new GeoLookupDto(q.Query.Trim(), "", region.Name, null, centre.ToDto(), region.ToDto());
        }
        return (await geoLocator.LookupAsync(q.Query, ct))?.ToDto()
            ?? throw new NotFoundException($"We couldn't find '{q.Query}'. Try a ZIP code or postcode, a city ('Dallas' or 'Portland, OR'), or a country, state or province.");
    }
}

// ---------- Sectors ----------

public sealed record GetSectorsQuery : IRequest<IReadOnlyList<string>>, ICacheableRequest
{
    public string CacheKey => "sectors";
    public string CacheProfile => "Reference";
}

public sealed class GetSectorsHandler(ICompanyRepository repository) : IRequestHandler<GetSectorsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> HandleAsync(GetSectorsQuery q, CancellationToken ct) => repository.GetSectorsAsync(ct);
}

// ---------- Data set info ----------

public sealed record GetDataMetaQuery : IRequest<DataMetaDto>;

public sealed class GetDataMetaHandler(ICompanyRepository repository) : IRequestHandler<GetDataMetaQuery, DataMetaDto>
{
    public async Task<DataMetaDto> HandleAsync(GetDataMetaQuery q, CancellationToken ct)
    {
        var meta = await repository.GetMetadataAsync(ct);
        var (companies, locations) = await repository.GetCountsAsync(ct);
        return new DataMetaDto(meta.DataVersion, meta.AsOfDate, meta.IsSampleData, companies, locations, meta.LoadedAt);
    }
}
