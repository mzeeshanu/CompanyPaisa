using CompanyPaisa.Importer.Uk;

namespace CompanyPaisa.Core.Tests;

/// <summary>Figures the xBRL-JSON conversion failed on are read from the report's HTML (Investor 2023).</summary>
public class IxbrlRepairTests
{
    private const string Html = """
        <ix:nonFraction name="ifrs-full:Revenue" id="fact-5" format="ixt4:num-comma-decimal" contextRef="c-1" unitRef="u-1" decimals="-6" scale="6">59&#x202F; 643</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:ProfitLoss" id="fact-30" format="ixt4:num-comma-decimal" contextRef="c-2" unitRef="u-1" decimals="-6" scale="6" sign="-">74 &#x202F; 7 6 2</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:CostOfSales" id="fact-9" format="ixt4:fixed-zero" contextRef="c-1" unitRef="u-1" decimals="-6" scale="6">–</ix:nonFraction>
        <ix:nonFraction name="ifrs-full:BasicEarningsLossPerShare" id="fact-40" format="ixt4:num-comma-decimal" contextRef="c-1" unitRef="u-2" decimals="2">41,39</ix:nonFraction>
        """;

    [Fact]
    public void Reads_figures_with_mixed_spaces_scale_and_sign()
    {
        var v = IxbrlRepair.Values(Html);
        Assert.Equal(59_643_000_000m, v["fact-5"]);
        Assert.Equal(-74_762_000_000m, v["fact-30"]);
        Assert.Equal(0m, v["fact-9"]);
        Assert.Equal(41.39m, v["fact-40"]);
    }

    [Fact]
    public void Fills_only_the_failed_values()
    {
        const string json = """
            {"facts": {
              "fact-5": {"value": "(ixTransformValueError)", "dimensions": {"concept": "ifrs-full:Revenue"}},
              "fact-30": {"value": "-1", "dimensions": {"concept": "ifrs-full:ProfitLoss"}},
              "fact-77": {"value": "(ixTransformValueError)", "dimensions": {"concept": "ifrs-full:Equity"}}
            }}
            """;
        Assert.True(IxbrlRepair.NeedsRepair(json));
        var (repaired, count) = IxbrlRepair.Repair(json, Html);
        Assert.Equal(1, count);
        Assert.Contains("\"59643000000\"", repaired);
        Assert.Contains("\"-1\"", repaired);   // a value that converted is left alone
        Assert.Contains("fact-77", repaired);
    }

    [Theory]
    [InlineData("1.234,5", "ixt:num-comma-decimal", "1234.5")]
    [InlineData("1,234.5", "ixt:num-dot-decimal", "1234.5")]
    [InlineData("12 345", "ixt4:num-dot-decimal", "12345")]
    [InlineData("n/a", "ixt:num-dot-decimal", null)]
    [InlineData("12", "ixt:date-day-month-year", null)]
    public void Reads_numbers_by_their_declared_format(string text, string format, string? expected) =>
        Assert.Equal(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), IxbrlRepair.Number(text, format));
}
