using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Client;

/// <summary>
/// Settings for apps that consume CompanyPaisa, e.g. in their appsettings.json:
/// <code>"CompanyPaisaApi": { "BaseUrl": "https://companypaisa.com/", "ApiKey": "…", "TimeoutSeconds": 30 }</code>
/// </summary>
public sealed class CompanyPaisaClientOptions
{
    [Required] public string BaseUrl { get; set; } = "";
    public string? ApiKey { get; set; }
    public string ApiKeyHeader { get; set; } = "X-Api-Key";
    [Range(1, 600)] public int TimeoutSeconds { get; set; } = 30;
}

public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ICompanyPaisaClient"/> using a configuration section (e.g. "CompanyPaisaApi").</summary>
    public static IHttpClientBuilder AddCompanyPaisaClient(this IServiceCollection services, IConfiguration section)
    {
        services.AddOptions<CompanyPaisaClientOptions>().Bind(section).ValidateDataAnnotations().ValidateOnStart();
        return services.AddCompanyPaisaHttpClient();
    }

    /// <summary>Registers <see cref="ICompanyPaisaClient"/> configured in code.</summary>
    public static IHttpClientBuilder AddCompanyPaisaClient(this IServiceCollection services, Action<CompanyPaisaClientOptions> configure)
    {
        services.AddOptions<CompanyPaisaClientOptions>().Configure(configure).ValidateDataAnnotations().ValidateOnStart();
        return services.AddCompanyPaisaHttpClient();
    }

    private static IHttpClientBuilder AddCompanyPaisaHttpClient(this IServiceCollection services) =>
        services.AddHttpClient<ICompanyPaisaClient, CompanyPaisaClient>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<CompanyPaisaClientOptions>>().Value;
            http.BaseAddress = new Uri(o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
            if (!string.IsNullOrEmpty(o.ApiKey)) http.DefaultRequestHeaders.Add(o.ApiKeyHeader, o.ApiKey);
        });
}
