using CompanyPaisa.Importer.Uk;

namespace CompanyPaisa.Core.Tests;

public class SavedTickerTests
{
    [Fact]
    public void A_company_on_the_saved_list_keeps_its_ticker_while_it_still_files()
    {
        var filers = new[] { new UkEntity("LEI-VOLVO", "Aktiebolaget Volvo", []), new UkEntity("LEI-NEW", "Newcomer AB", []) };
        var saved = new[] { new UkConstituent("VOLVB", "Aktiebolaget Volvo", "SE", "LEI-VOLVO"), new UkConstituent("GONE", "Stopped Filing AB", "SE", "LEI-GONE") };
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var kept = UkMainMarket.KeepSaved(filers, saved, taken);

        var only = Assert.Single(kept);
        Assert.Equal(("VOLVB", "LEI-VOLVO"), (only.Ticker, only.Lei));
        Assert.Equal(["VOLVB"], taken);
    }
}
