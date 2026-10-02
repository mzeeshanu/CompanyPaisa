using CompanyPaisa.Core.Abstractions;
using Microsoft.Extensions.Hosting;

namespace CompanyPaisa.Infrastructure.Services;

/// <summary>Resolves relative paths against the host's content root (the API project folder in development).</summary>
public sealed class ContentRootPathResolver(IHostEnvironment environment) : IFilePathResolver
{
    public string Resolve(string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));
}
