using System.Text.RegularExpressions;

namespace CompanyPaisa.Api.Tests;

/// <summary>
/// Endpoints, filters and middleware get their services as parameters, so what each one needs shows in its signature and a
/// test can hand it something else (CP-37). Only the composition root may resolve services itself.
/// </summary>
public class InjectedServicesTests
{
    private static string ApiSource([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "src", "CompanyPaisa.Api"));

    [Fact]
    public void The_api_does_not_look_services_up_from_the_request()
    {
        var lookups = Directory.EnumerateFiles(ApiSource(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(l => Regex.IsMatch(l.Text, @"RequestServices|\bIServiceProvider\s+services\b"))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.Empty(lookups);
    }
}
