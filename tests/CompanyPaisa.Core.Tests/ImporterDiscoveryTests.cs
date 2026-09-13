using System.Globalization;
using System.Text.RegularExpressions;
using CompanyPaisa.Importer;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Core.Tests;

public partial class DiscoveryTests
{
    /// <summary>
    /// Fake EDGAR search: 7 filers a day, with deliberately unstable paging (later pages overlap), so anything that
    /// relies on deep paging would miss filers.
    /// </summary>
    private sealed partial class FakeSearch : ISecClient
    {
        public const int PerDay = 7;
        public int NetworkRequests { get; private set; }

        [GeneratedRegex(@"startdt=(\d{4}-\d{2}-\d{2})&enddt=(\d{4}-\d{2}-\d{2})&from=(\d+)")]
        private static partial Regex Query();

        public Task<string?> GetStringAsync(string url, CachePolicy policy, CancellationToken ct = default)
        {
            NetworkRequests++;
            var m = Query().Match(url);
            var start = DateOnly.ParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = DateOnly.ParseExact(m.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var from = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            var all = Enumerable.Range(start.DayNumber, end.DayNumber - start.DayNumber + 1)
                .SelectMany(d => Enumerable.Range(0, PerDay).Select(i => (long)d * 10 + i)).ToList();
            var page = all.Skip(from == 0 ? 0 : from - 20).Take(100);   // unstable paging: later pages overlap
            var hits = string.Join(',', page.Select(c => $$$"""{"_source":{"ciks":["{{{c:D10}}}"]}}"""));
            return Task.FromResult<string?>($$$"""{"hits":{"total":{"value":{{{all.Count}}}},"hits":[{{{hits}}}]}}""");
        }

        public Task<byte[]?> GetBytesAsync(string url, CachePolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Splits_the_date_range_so_no_filer_is_lost_to_unstable_paging()
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-120);
        var options = Microsoft.Extensions.Options.Options.Create(new ImporterOptions { Discovery = { States = ["TX"], Forms = ["10-K"], FiledSince = since } });
        var client = new FakeSearch();

        var ciks = await new EdgarService(client, options, NullLogger<EdgarService>.Instance).DiscoverFilerCiksAsync(CancellationToken.None);

        var days = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - since.DayNumber + 1;
        Assert.Equal(days * FakeSearch.PerDay, ciks.Count);      // every filer, once
        Assert.True(client.NetworkRequests < 40, $"{client.NetworkRequests} requests");
    }
}
