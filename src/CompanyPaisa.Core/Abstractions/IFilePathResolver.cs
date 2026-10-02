namespace CompanyPaisa.Core.Abstractions;

/// <summary>Turns relative paths from configuration into absolute paths (relative to the app's content root).</summary>
public interface IFilePathResolver
{
    string Resolve(string path);
}
