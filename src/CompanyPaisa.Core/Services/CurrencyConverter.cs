using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Core.Services;

/// <summary>
/// appsettings section "Currency": approximate exchange rates, only for adding up and ranking amounts that are in
/// different currencies (a London search mixes Shell's dollars and Tesco's pounds). Company figures themselves are always
/// shown in the currency they were reported in.
/// </summary>
public sealed class CurrencyOptions
{
    public const string SectionName = "Currency";

    /// <summary>US dollars per one unit of each currency, e.g. { "USD": 1, "GBP": 1.27, "EUR": 1.08 }.</summary>
    [MinLength(1)] public Dictionary<string, decimal> UsdPer { get; set; } = new(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m };
}

public interface ICurrencyConverter
{
    /// <summary>The amount in US dollars (unchanged if the currency has no configured rate).</summary>
    decimal ToUsd(decimal amount, string currency);
    decimal Convert(decimal amount, string from, string to);
    /// <summary>The currency most of the items use — what a mixed total is expressed in.</summary>
    string Dominant(IEnumerable<string> currencies);
}

public sealed class CurrencyConverter(IOptionsMonitor<CurrencyOptions> options) : ICurrencyConverter
{
    private decimal Rate(string currency) => options.CurrentValue.UsdPer.TryGetValue(currency, out var r) && r > 0 ? r : 1m;

    public decimal ToUsd(decimal amount, string currency) => amount * Rate(currency);

    public decimal Convert(decimal amount, string from, string to) =>
        string.Equals(from, to, StringComparison.OrdinalIgnoreCase) ? amount : Math.Round(amount * Rate(from) / Rate(to), 0);

    public string Dominant(IEnumerable<string> currencies) =>
        currencies.GroupBy(c => c, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault()?.Key ?? "USD";
}
