using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Sec;

/// <summary>How long a downloaded response stays valid in the local cache.</summary>
public enum CachePolicy
{
    /// <summary>Filings never change once published — cache forever.</summary>
    Immutable,
    /// <summary>Indexes and company records change — refresh after Sec:IndexCacheHours.</summary>
    Index
}

/// <summary>Polite HTTP access to SEC / Census: identifies itself, stays under the rate limit, retries, and caches to disk.</summary>
public interface ISecClient
{
    Task<string?> GetStringAsync(string url, CachePolicy policy, CancellationToken ct = default);
    Task<byte[]?> GetBytesAsync(string url, CachePolicy policy, CancellationToken ct = default);
    int NetworkRequests { get; }
}

public sealed class SecClient : ISecClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly SecOptions _options;
    private readonly string _cacheDir;
    private readonly ILogger<SecClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _nextSlot = DateTime.MinValue;
    private int _requests;

    public SecClient(IOptions<ImporterOptions> options, RepoPaths paths, ILogger<SecClient> logger)
    {
        _options = options.Value.Sec;
        _logger = logger;
        _cacheDir = paths.Resolve(_options.CacheDirectory);
        Directory.CreateDirectory(_cacheDir);
        _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        // The SEC's required format ("Company contact@email") isn't a valid product token, so skip header validation.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
    }

    public int NetworkRequests => _requests;

    public async Task<string?> GetStringAsync(string url, CachePolicy policy, CancellationToken ct = default)
    {
        var bytes = await GetBytesAsync(url, policy, ct);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    public async Task<byte[]?> GetBytesAsync(string url, CachePolicy policy, CancellationToken ct = default)
    {
        var file = Path.Combine(_cacheDir, Hash(url));
        if (File.Exists(file))
        {
            var fresh = policy == CachePolicy.Immutable ||
                        DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromHours(_options.IndexCacheHours);
            if (fresh) return Unpack(await File.ReadAllBytesAsync(file, ct));
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await WaitForSlotAsync(ct);
            Interlocked.Increment(ref _requests);
            try
            {
                using var response = await _http.GetAsync(url, ct);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Forbidden)
                {
                    var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2);
                    _logger.LogWarning("{Status} from {Url}; waiting {Wait}s (attempt {Attempt})", (int)response.StatusCode, url, wait.TotalSeconds, attempt);
                    await Task.Delay(wait, ct);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                await File.WriteAllBytesAsync(file, _options.CompressCache ? Pack(bytes) : bytes, ct);
                return bytes;
            }
            catch (HttpRequestException ex) when (attempt < 5)
            {
                _logger.LogWarning("Request to {Url} failed ({Message}); retrying", url, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < 5)
            {
                _logger.LogWarning("Request to {Url} timed out; retrying", url);
            }
        }

        _logger.LogError("Giving up on {Url}", url);
        return null;
    }

    /// <summary>Spaces requests evenly so we never exceed MaxRequestsPerSecond.</summary>
    private async Task WaitForSlotAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            if (_nextSlot > now) await Task.Delay(_nextSlot - now, ct);
            _nextSlot = DateTime.UtcNow.AddMilliseconds(1000.0 / _options.MaxRequestsPerSecond);
        }
        finally { _gate.Release(); }
    }

    // Cache files are gzip when CompressCache is on. Files are detected by the gzip magic bytes, so plain files from
    // older runs still work — and already-compressed downloads (like the Census .zip) are stored as-is.
    private static bool IsGzip(byte[] b) => b.Length > 2 && b[0] == 0x1F && b[1] == 0x8B;

    internal static byte[] Pack(byte[] bytes)
    {
        if (IsGzip(bytes) || (bytes.Length > 1 && bytes[0] == 'P' && bytes[1] == 'K')) return bytes;
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Fastest)) gz.Write(bytes);
        return output.ToArray();
    }

    internal static byte[] Unpack(byte[] bytes)
    {
        if (!IsGzip(bytes)) return bytes;
        using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }

    private static string Hash(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32];

    public void Dispose() => _http.Dispose();
}

/// <summary>Resolves paths from appsettings relative to the repository root (found by walking up to the .slnx).</summary>
public sealed class RepoPaths
{
    public RepoPaths()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.slnx").Any()) dir = dir.Parent;
        Root = dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    public string Root { get; }
    public string Resolve(string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));
}
