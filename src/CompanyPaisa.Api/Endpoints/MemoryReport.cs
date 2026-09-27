using CompanyPaisa.Api.Analytics;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Data;

namespace CompanyPaisa.Api.Endpoints;

/// <summary>What the process is holding, in bytes, next to the rows it is holding them for.</summary>
/// <param name="ManagedBytes">Objects on the .NET heap right now.</param>
/// <param name="CommittedBytes">What the heap has taken from the operating system (closer to what the host bills for).</param>
/// <param name="WorkingSetBytes">The whole process as the operating system sees it: heap, the runtime, and SQLite's own pages.</param>
/// <param name="HeapLimitBytes">The ceiling the collector is working to (0 when it isn't capped), from the container's limit.</param>
/// <param name="Gc">Whether Server GC is on, and how many times each generation has been collected.</param>
/// <param name="Rows">Rows of each kind in the loaded data set, so a number can be read per row.</param>
public sealed record MemoryReportResponse(
    long ManagedBytes,
    long CommittedBytes,
    long WorkingSetBytes,
    long HeapLimitBytes,
    GcReport Gc,
    IReadOnlyList<DataRowCount> Rows);

/// <summary>How the collector is configured and how hard it has been working.</summary>
public sealed record GcReport(bool ServerMode, bool Concurrent, int Gen0, int Gen1, int Gen2, double PauseTimePercentage);

public static class MemoryReport
{
    /// <summary>
    /// <c>GET /health/memory</c>: what the process is holding and what it is holding it for, behind the same key as the
    /// analytics dashboard (so it is a 404 until one is configured). Numbers only — it reads no company or visitor data.
    /// </summary>
    public static IEndpointRouteBuilder MapMemoryReport(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/memory", (ICompanyRepository repository) =>
            {
                var info = GC.GetGCMemoryInfo();
                return Results.Ok(new MemoryReportResponse(
                    GC.GetTotalMemory(forceFullCollection: false),
                    info.TotalCommittedBytes,
                    Environment.WorkingSet,
                    info.TotalAvailableMemoryBytes,
                    new GcReport(System.Runtime.GCSettings.IsServerGC, System.Runtime.GCSettings.LatencyMode != System.Runtime.GCLatencyMode.Batch,
                        GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), info.PauseTimePercentage),
                    // Only a repository that keeps the data set in memory has rows to report.
                    repository is IDataRowCounts counts ? counts.RowCounts() : []));
            })
            .AddEndpointFilter(AnalyticsEndpoints.RequireDashboardKey)
            .ExcludeFromDescription();
        return app;
    }
}
