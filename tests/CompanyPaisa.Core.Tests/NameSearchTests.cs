using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Tests;

public class NameSearchTests
{
    private static (FakeRepository Repo, NameSearchIndex Index) Build()
    {
        var repo = new FakeRepository()
            .Add("NVDA", "Semiconductors", LocationType.Headquarters, 37.37, -121.96, 130_000_000_000m)
            .Add("NVMI", "Semiconductors", LocationType.Headquarters, 32.1, 34.8, 600_000_000m)
            .Add("BAC", "Banking", LocationType.Headquarters, 35.2, -80.8, 100_000_000_000m)
            .Add("SOGN", "Banking", LocationType.Headquarters, 48.9, 2.2, 30_000_000_000m);
        repo.Companies[0] = repo.Companies[0] with { Name = "NVIDIA Corporation" };
        repo.Companies[1] = repo.Companies[1] with { Name = "Nova Ltd" };
        repo.Companies[2] = repo.Companies[2] with { Name = "Bank of America Corp" };
        repo.Companies[3] = repo.Companies[3] with { Name = "Société Générale S.A.", Ticker = "GLE.PA" };
        repo.Pay("tim-cook-1", "NVDA", "Chief Executive Officer", 2023, 2025, 60_000_000m)
            .Pay("tim-cooper-2", "BAC", "Chief Financial Officer", 2024, 2025, 9_000_000m)
            .Pay("ann-timms-3", "BAC", "General Counsel", 2025, 2025, 4_000_000m);
        foreach (var e in repo.Executives.ToList())
        {
            var name = e.PersonId switch { "tim-cook-1" => "Tim Cook", "tim-cooper-2" => "Tim Cooper", _ => "Ann Timms" };
            repo.Executives[repo.Executives.IndexOf(e)] = e with { ExecutiveName = name };
        }
        var index = new NameSearchIndex(repo, new FinancialMetricsService(Opt.Monitor(new MetricsOptions())),
            new CurrencyConverter(Opt.Monitor(new CurrencyOptions())));
        return (repo, index);
    }

    [Theory]
    [InlineData("nvda", "NVDA")]              // ticker
    [InlineData("nvidia", "NVDA")]            // name, any case
    [InlineData("bank am", "BAC")]            // every word starts a word of the name
    [InlineData("societe generale", "GLE.PA")] // accents ignored
    [InlineData("GLE", "GLE.PA")]             // ticker without its exchange suffix
    public async Task Companies_are_found_by_name_or_ticker(string query, string expected)
    {
        var result = await Build().Index.SearchAsync(query, 5, includeExecutives: true);
        Assert.Equal(expected, result.Companies[0].Ticker);
    }

    [Fact]
    public async Task Better_matches_come_first_and_bigger_companies_break_ties()
    {
        var result = await Build().Index.SearchAsync("n", 5, includeExecutives: false);
        Assert.Equal(["NVDA", "NVMI"], result.Companies.Select(c => c.Ticker).Take(2));
    }

    [Fact]
    public async Task People_are_found_by_first_and_last_name_with_their_latest_role()
    {
        var result = await Build().Index.SearchAsync("tim coo", 5, includeExecutives: true);

        Assert.Equal(["Tim Cook", "Tim Cooper"], result.Executives.Select(e => e.Name));   // higher pay first
        var cook = result.Executives[0];
        Assert.Equal(2025, cook.LatestYear);
        Assert.Equal("NVIDIA Corporation", cook.Company.Name);
        Assert.Equal("Chief Executive Officer", cook.Title);
        Assert.DoesNotContain(result.Executives, e => e.Name == "Ann Timms");   // "coo" starts none of her names
    }

    [Fact]
    public async Task Executives_can_be_left_out_and_nonsense_finds_nothing()
    {
        var (_, index) = Build();
        Assert.Empty((await index.SearchAsync("tim", 5, includeExecutives: false)).Executives);
        var none = await index.SearchAsync("zzzz", 5, includeExecutives: true);
        Assert.Empty(none.Companies);
        Assert.Empty(none.Executives);
    }

    [Fact]
    public async Task A_reloaded_data_set_rebuilds_the_index()
    {
        var (repo, _) = Build();
        var index = new NameSearchIndex(new ReloadedRepository(repo), new FinancialMetricsService(Opt.Monitor(new MetricsOptions())),
            new CurrencyConverter(Opt.Monitor(new CurrencyOptions())));
        Assert.Empty((await index.SearchAsync("acme", 5, false)).Companies);

        repo.Companies.Add(new Company { CompanyId = "ACME", Name = "Acme Rockets", Ticker = "ACME", Exchange = "NYSE", Sector = "Aerospace" });

        Assert.Single((await index.SearchAsync("acme", 5, false)).Companies);
    }

    [Theory]
    [InlineData("Société Générale S.A.", "societe generale s a")]
    [InlineData("AT&T Inc.", "at t inc")]
    [InlineData("O'Reilly Automotive", "oreilly automotive")]
    public void Names_are_normalized(string name, string expected) => Assert.Equal(expected, NameSearchIndex.Normalize(name));

    /// <summary>Hands out a new company list on every call, the way a data reload swaps in a new snapshot.</summary>
    private sealed class ReloadedRepository(FakeRepository inner) : Abstractions.ICompanyRepository
    {
        public Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Company>>(inner.Companies.ToList());
        public Task<Company?> GetCompanyAsync(string id, CancellationToken ct = default) => inner.GetCompanyAsync(id, ct);
        public Task<IReadOnlyList<Company>> GetCompaniesAsync(IEnumerable<string> ids, CancellationToken ct = default) => inner.GetCompaniesAsync(ids, ct);
        public Task<IReadOnlyList<CompanyLocation>> GetLocationsWithinAsync(GeoBoundingBox box, CancellationToken ct = default) => inner.GetLocationsWithinAsync(box, ct);
        public Task<IReadOnlyList<CompanyLocation>> GetLocationsAsync(string id, CancellationToken ct = default) => inner.GetLocationsAsync(id, ct);
        public Task<IReadOnlyList<FinancialPeriod>> GetFinancialsAsync(string id, CancellationToken ct = default) => inner.GetFinancialsAsync(id, ct);
        public Task<IReadOnlyDictionary<string, IReadOnlyList<FinancialPeriod>>> GetFinancialsAsync(IEnumerable<string> ids, CancellationToken ct = default) => inner.GetFinancialsAsync(ids, ct);
        public Task<IReadOnlyList<ExecutiveCompensation>> GetExecutiveCompensationAsync(string id, CancellationToken ct = default) => inner.GetExecutiveCompensationAsync(id, ct);
        public Task<IReadOnlyList<ExecutiveCompensation>> GetExecutiveCompensationAsync(IEnumerable<string> ids, CancellationToken ct = default) => inner.GetExecutiveCompensationAsync(ids, ct);
        public Task<IReadOnlyList<ExecutiveCompensation>> GetCompensationForPeopleAsync(IEnumerable<string> ids, CancellationToken ct = default) => inner.GetCompensationForPeopleAsync(ids, ct);
        public Task<Person?> GetPersonAsync(string id, CancellationToken ct = default) => inner.GetPersonAsync(id, ct);
        public Task<IReadOnlyList<string>> GetSectorsAsync(CancellationToken ct = default) => inner.GetSectorsAsync(ct);
        public Task<DataSetMetadata> GetMetadataAsync(CancellationToken ct = default) => inner.GetMetadataAsync(ct);
        public Task<(int Companies, int Locations)> GetCountsAsync(CancellationToken ct = default) => inner.GetCountsAsync(ct);
    }
}
