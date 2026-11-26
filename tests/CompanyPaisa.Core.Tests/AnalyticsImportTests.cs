using CompanyPaisa.Analytics;
using CompanyPaisa.Analytics.Postgres;
using CompanyPaisa.Analytics.Sqlite;
using CompanyPaisa.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CompanyPaisa.Core.Tests;

/// <summary>Analytics:ImportFrom copies the history from another provider once, all or nothing.</summary>
public sealed class AnalyticsImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("analytics-import-").FullName;
    private string File(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private static AnalyticsEvent Event(int day, string name, string visitor, string? subject = null) =>
        new(new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.Zero), name, visitor, subject, subject is null ? null : $"{subject} Inc.",
            null, null, null, "US", "Utah", "Lehi", "Desktop", "Chrome", "Windows", "google.com", "/", "website");

    private static async Task<SqliteAnalyticsStore> StoreWith(string path, params AnalyticsEvent[] events)
    {
        var store = new SqliteAnalyticsStore(path);
        await store.InitializeAsync(CancellationToken.None);
        if (events.Length > 0) await store.WriteAsync(events, CancellationToken.None);
        return store;
    }

    private static ServiceProvider Build(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>($"Analytics:{s.Key}", s.Value)))
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IFilePathResolver>(new AbsolutePaths());
        services.AddCompanyPaisaAnalytics(configuration, a => a.AddSqlite().AddPostgres());
        return services.BuildServiceProvider();
    }

    private static async Task<List<AnalyticsEvent>> All(IAnalyticsReader reader) =>
        await reader.GetEventsAsync(DateOnly.MinValue, DateOnly.MaxValue, CancellationToken.None).ToListAsync();

    [Fact]
    public async Task Copies_every_event_in_order_into_an_empty_store_once()
    {
        AnalyticsEvent[] history = [Event(1, "page_view", "v1"), Event(2, "company_view", "v1", "ADBE"), Event(30, "search", "v2")];
        await StoreWith(File("old.db"), history);

        await using var sp = Build(("Provider", "Sqlite"), ("SqlitePath", File("new.db")),
            ("ImportFrom:Provider", "Sqlite"), ("ImportFrom:SqlitePath", File("old.db")));
        var target = sp.GetRequiredService<IAnalyticsDatabase>();
        await target.InitializeAsync(CancellationToken.None);
        var import = sp.GetRequiredService<AnalyticsImport>();

        await import.RunAsync(target, CancellationToken.None);
        Assert.Equal(3, import.Copied);
        Assert.Equal(history, await All(target));

        // A restart: the store has events now, so nothing is copied twice.
        await using var restarted = Build(("Provider", "Sqlite"), ("SqlitePath", File("new.db")),
            ("ImportFrom:Provider", "Sqlite"), ("ImportFrom:SqlitePath", File("old.db")));
        var again = restarted.GetRequiredService<AnalyticsImport>();
        await again.RunAsync(restarted.GetRequiredService<IAnalyticsDatabase>(), CancellationToken.None);
        Assert.Null(again.Copied);
        Assert.Equal(3, (await All(target)).Count);
    }

    [Fact]
    public async Task A_store_that_already_has_events_is_left_alone()
    {
        await StoreWith(File("old.db"), Event(1, "page_view", "v1"), Event(2, "page_view", "v2"));
        var target = await StoreWith(File("new.db"), Event(20, "page_view", "v9"));

        await using var sp = Build(("Provider", "Sqlite"), ("SqlitePath", File("new.db")),
            ("ImportFrom:Provider", "Sqlite"), ("ImportFrom:SqlitePath", File("old.db")));
        var import = sp.GetRequiredService<AnalyticsImport>();
        await import.RunAsync(target, CancellationToken.None);

        Assert.Null(import.Copied);
        Assert.Equal("v9", Assert.Single(await All(target)).Visitor);
    }

    [Fact]
    public async Task A_source_that_cannot_be_read_copies_nothing_and_does_not_stop_recording()
    {
        var target = await StoreWith(File("new.db"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("ImportFrom:SqlitePath", File("not-an-analytics-file.db"))]).Build();   // no tables in it
        var services = new ServiceCollection().AddSingleton<IFilePathResolver>(new AbsolutePaths()).BuildServiceProvider();
        var import = new AnalyticsImport(services, NullLogger<AnalyticsImport>.Instance,
            new AnalyticsImportSource(new SqliteAnalyticsAdapter(), config.GetSection("ImportFrom")), retries: [TimeSpan.Zero]);

        await import.RunAsync(target, CancellationToken.None);   // no exception

        Assert.Null(import.Copied);
        Assert.False(await target.HasEventsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Postgres_source_without_a_connection_string_copies_nothing()
    {
        await using var sp = Build(("Provider", "Sqlite"), ("SqlitePath", File("new.db")), ("ImportFrom:Provider", "Postgres"));
        var target = sp.GetRequiredService<IAnalyticsDatabase>();
        await target.InitializeAsync(CancellationToken.None);
        var import = sp.GetRequiredService<AnalyticsImport>();

        await import.RunAsync(target, CancellationToken.None);

        Assert.Null(import.Copied);
    }

    [Fact]
    public void An_unknown_import_provider_stops_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Build(("Provider", "Sqlite"), ("SqlitePath", File("new.db")), ("ImportFrom:Provider", "Mysql")));
        Assert.Contains("'Mysql'", ex.Message);
    }

    [Fact]
    public async Task Without_import_settings_nothing_happens()
    {
        await using var sp = Build(("Provider", "Sqlite"), ("SqlitePath", File("new.db")));
        var target = sp.GetRequiredService<IAnalyticsDatabase>();
        await target.InitializeAsync(CancellationToken.None);
        var import = sp.GetRequiredService<AnalyticsImport>();

        await import.RunAsync(target, CancellationToken.None);

        Assert.Null(import.Copied);
        Assert.False(await target.HasEventsAsync(CancellationToken.None));
    }

    private sealed class AbsolutePaths : IFilePathResolver
    {
        public string Resolve(string path) => Path.GetFullPath(path);
    }
}
