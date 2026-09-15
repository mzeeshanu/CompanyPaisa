using CompanyPaisa.Importer.Validation;
using Microsoft.Extensions.Logging;

namespace CompanyPaisa.Importer.Publishing;

/// <summary>
/// One market: a source of listed companies that the importer turns into rows of the website's database. Each market
/// finds its companies, reads their figures and publishes them with <see cref="DataPublisher"/> under its own id, so
/// markets can be rebuilt independently. Adding a country means one more implementation, registered in Program.cs.
/// </summary>
public interface IMarketImporter
{
    /// <summary>The market's id in the database and on the command line ("sec", "uk", "eu"…).</summary>
    string Market { get; }

    /// <summary>One line for --list-markets and logs.</summary>
    string Description { get; }

    /// <param name="refreshLists">Re-read curated company lists (e.g. the FTSE constituents) instead of using the saved ones.</param>
    Task<int> RunAsync(bool refreshLists, CancellationToken ct);
}

/// <summary>
/// Runs markets by id (--market sec), or every one of them followed by the data-quality checks (--all). A market that
/// fails stops the run, so the database never mixes a half-finished refresh with published data.
/// </summary>
public sealed class MarketRunner(IEnumerable<IMarketImporter> markets, ValidationRun validation, ILogger<MarketRunner> logger)
{
    public IReadOnlyList<IMarketImporter> Markets { get; } = markets.ToList();

    public async Task<int> RunAsync(IReadOnlyList<string> ids, bool refreshLists, bool strictValidation, CancellationToken ct)
    {
        var selected = ids.Count == 0 ? Markets : ids.Select(Find).ToList();
        foreach (var market in selected)
        {
            logger.LogInformation("Market '{Market}': {Description}", market.Market, market.Description);
            var exit = await market.RunAsync(refreshLists, ct);
            if (exit != 0) { logger.LogError("Market '{Market}' failed (exit code {Exit}); stopping", market.Market, exit); return exit; }
        }
        return ids.Count == 0 ? await validation.RunAsync(strictValidation, ct) : 0;
    }

    private IMarketImporter Find(string id) =>
        Markets.FirstOrDefault(m => string.Equals(m.Market, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown market '{id}'. Known: {string.Join(", ", Markets.Select(m => m.Market))}.");
}
