using CompanyPaisa.Api.Composition;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Data.Excel;
using CompanyPaisa.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyPaisa.Api.Tests;

/// <summary>DataSource:Provider picks the repository, in any case, and a wrong name stops the app with the names it accepts.</summary>
public sealed class DataSourceProviderTests
{
    private static Type? RegisteredRepository(string? provider)
    {
        var settings = new Dictionary<string, string?>();
        if (provider is not null) settings["DataSource:Provider"] = provider;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddCompanyPaisaDataSource(configuration);
        return services.Single(d => d.ServiceType == typeof(ICompanyRepository)).ImplementationType;
    }

    [Theory]
    [InlineData("Sqlite", typeof(SqliteCompanyRepository))]
    [InlineData("sqlite", typeof(SqliteCompanyRepository))]
    [InlineData("Excel", typeof(ExcelCompanyRepository))]
    [InlineData("EXCEL", typeof(ExcelCompanyRepository))]
    [InlineData(null, typeof(SqliteCompanyRepository))]
    public void Provider_names_any_case_and_defaults_to_Sqlite(string? provider, Type expected) =>
        Assert.Equal(expected, RegisteredRepository(provider));

    [Theory]
    [InlineData("Postgres")]
    [InlineData("")]
    [InlineData("1")]
    public void Unknown_provider_fails_with_the_supported_names(string provider)
    {
        var error = Assert.Throws<InvalidOperationException>(() => RegisteredRepository(provider));
        Assert.Contains($"'{provider}' isn't supported", error.Message);
        Assert.Contains("Supported: Sqlite, Excel", error.Message);
    }
}
