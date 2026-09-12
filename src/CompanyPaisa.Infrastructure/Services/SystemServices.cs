using CompanyPaisa.Core.Abstractions;
using Microsoft.Extensions.Hosting;

namespace CompanyPaisa.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Resolves relative paths against the host's content root (the API project folder in development).</summary>
public sealed class ContentRootPathResolver(IHostEnvironment environment) : IFilePathResolver
{
    public string Resolve(string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));
}

public sealed class DataChangeSignal : IDataChangeSignal
{
    private long _generation;
    public long Generation => Interlocked.Read(ref _generation);
    public void Signal() => Interlocked.Increment(ref _generation);
}
