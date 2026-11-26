using CompanyPaisa.Analytics;
using CompanyPaisa.Analytics.Postgres;
using CompanyPaisa.Analytics.Sqlite;
using CompanyPaisa.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompanyPaisa.Core.Tests;

/// <summary>Analytics:Provider picks a registered adapter by name; the rest of the app only sees the interfaces.</summary>
public class AnalyticsAdapterTests
{
    private static (IServiceCollection Services, AnalyticsStatus Status) Register(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>($"Analytics:{s.Key}", s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IFilePathResolver>(new AbsolutePaths());
        var status = services.AddCompanyPaisaAnalytics(configuration, a => a.AddSqlite().AddPostgres());
        return (services, status);
    }

    [Fact]
    public void An_unknown_provider_stops_startup_and_lists_the_ones_this_build_has()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Register(("Provider", "Mongo")));
        Assert.Contains("'Mongo'", ex.Message);
        Assert.Contains("None, Postgres, Sqlite", ex.Message);
    }

    [Fact]
    public void None_records_nothing_and_registers_no_store()
    {
        var (services, status) = Register(("Provider", "None"));
        Assert.Equal(new AnalyticsStatus("None", false, "Analytics:Provider is None."), status);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAnalyticsStore));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void Postgres_without_a_connection_string_turns_analytics_off_instead_of_crashing()
    {
        var (services, status) = Register(("Provider", "Postgres"));
        Assert.False(status.Enabled);
        Assert.Equal("Postgres", status.Provider);
        Assert.Contains("Analytics:ConnectionString is empty", status.Message);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IAnalyticsStore));
    }

    [Fact]
    public async Task Sqlite_is_picked_by_name_ignoring_case_and_serves_both_interfaces()
    {
        var path = Path.Combine(Path.GetTempPath(), $"analytics-adapter-{Guid.NewGuid():N}.db");
        try
        {
            var (services, status) = Register(("Provider", "sqlite"), ("SqlitePath", path));
            Assert.Equal(new AnalyticsStatus("Sqlite", true, "Recording to Sqlite."), status);

            await using var provider = services.BuildServiceProvider();
            var store = provider.GetRequiredService<IAnalyticsStore>();
            Assert.IsType<SqliteAnalyticsStore>(store);
            Assert.Same(store, provider.GetRequiredService<IAnalyticsReader>());
            Assert.Equal(path, ((SqliteAnalyticsStore)store).Path);
            Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(AnalyticsWriter));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public void A_new_provider_is_one_adapter_and_one_registration()
    {
        var registry = new AnalyticsAdapters().AddSqlite().Add(new FakeAdapter());
        Assert.Equal(["None", "Fake", "Sqlite"], registry.Names);
        Assert.IsType<FakeAdapter>(registry.Get("FAKE"));
        Assert.Null(registry.Get("none"));
        Assert.Throws<ArgumentException>(() => registry.Add(new FakeAdapter("None")));
    }

    private sealed class AbsolutePaths : IFilePathResolver
    {
        public string Resolve(string path) => Path.GetFullPath(path);
    }

    private sealed class FakeAdapter(string name = "Fake") : IAnalyticsAdapter
    {
        public string Name => name;
        public string? Problem(IConfigurationSection settings) => null;
        public IAnalyticsDatabase Create(IServiceProvider services, IConfigurationSection settings) => throw new NotSupportedException();
    }
}
