using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Mapping;
using CompanyPaisa.Core.Messaging;

namespace CompanyPaisa.Core.Features.Reference;

// ---------- ZIP / city lookup ----------

public sealed record LookupGeoQuery(string Query) : IRequest<GeoLookupDto>;

public sealed class LookupGeoValidator : IRequestValidator<LookupGeoQuery>
{
    public IEnumerable<ValidationError> Validate(LookupGeoQuery q)
    {
        if (string.IsNullOrWhiteSpace(q.Query) || q.Query.Length > 100)
            yield return new("query", "Enter a US ZIP code, a UK postcode or 'City, ST'.");
    }
}

public sealed class LookupGeoHandler(IGeoLocator geoLocator) : IRequestHandler<LookupGeoQuery, GeoLookupDto>
{
    public async Task<GeoLookupDto> HandleAsync(LookupGeoQuery q, CancellationToken ct) =>
        (await geoLocator.LookupAsync(q.Query, ct))?.ToDto()
        ?? throw new NotFoundException($"We couldn't find '{q.Query}'. Try a 5-digit US ZIP code, a Canadian or UK postcode, or 'City, ST'.");
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
