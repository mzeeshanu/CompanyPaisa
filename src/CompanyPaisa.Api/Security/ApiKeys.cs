using System.Security.Cryptography;
using System.Text;
using CompanyPaisa.Api.Options;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Security;

/// <summary>Who is calling: a named API client, an anonymous caller, or someone with a bad key.</summary>
public sealed record ApiCaller(string? ClientName, bool KeyPresented, bool KeyValid, string PartitionKey)
{
    public bool IsKeyed => ClientName is not null;
}

public interface IApiKeyValidator
{
    ApiCaller Identify(HttpContext context);
}

public sealed class ApiKeyValidator(IOptionsMonitor<ApiOptions> options) : IApiKeyValidator
{
    public ApiCaller Identify(HttpContext context)
    {
        var o = options.CurrentValue;
        if (context.Items.TryGetValue(typeof(ApiCaller), out var cached) && cached is ApiCaller known) return known;

        ApiCaller caller;
        var ip = ClientIp(context, o.ClientIpHeader);
        var presented = context.Request.Headers[o.ApiKeyHeader].ToString();
        if (string.IsNullOrEmpty(presented))
        {
            caller = new ApiCaller(null, false, false, $"ip:{ip}");
        }
        else
        {
            var match = o.Keys.FirstOrDefault(k => k.Enabled && FixedTimeEquals(k.Key, presented));
            caller = match is null
                ? new ApiCaller(null, true, false, $"badkey:{ip}")
                : new ApiCaller(match.Name, true, true, $"key:{match.Name}");
        }

        context.Items[typeof(ApiCaller)] = caller;
        return caller;
    }

    /// <summary>The visitor's IP: from the CDN's header when configured and valid, else the connection's address.</summary>
    public static string ClientIp(HttpContext context, string? header)
    {
        if (!string.IsNullOrWhiteSpace(header) && context.Request.Headers[header].ToString() is { Length: > 0 } value &&
            System.Net.IPAddress.TryParse(value.Split(',')[0].Trim(), out var fromCdn))
            return fromCdn.ToString();
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

/// <summary>
/// Who may call the API: an app with a valid key; otherwise only the website's own pages (their <see cref="SiteSessions"/>
/// pass), and never a script or scraper without a key. With Api:AllowAnonymous on (development), anyone.
/// </summary>
public sealed class ApiKeyEndpointFilter(IApiKeyValidator validator, SiteSessions sessions, IOptionsMonitor<ApiOptions> options) : IEndpointFilter
{
    /// <summary>Problem "code" the website reacts to by fetching a new pass and trying once more.</summary>
    public const string SessionRequired = "site_session_required";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var o = options.CurrentValue;
        var http = context.HttpContext;
        var caller = validator.Identify(http);
        if (caller.KeyPresented && !caller.KeyValid)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid API key",
                detail: $"The key in the '{o.ApiKeyHeader}' header isn't recognised.");
        if (caller.IsKeyed || o.AllowAnonymous) return await next(context);

        if (BotRules.IsBlocked(http, o))
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "API key required",
                detail: $"Apps and scripts need an API key: send it in the '{o.ApiKeyHeader}' header.");

        var (valid, ageing) = sessions.Check(http);
        if (!valid || !SiteSessions.FromOwnPage(http))
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "API key required",
                detail: $"Send your key in the '{o.ApiKeyHeader}' header.",
                extensions: new Dictionary<string, object?> { ["code"] = SessionRequired });
        if (ageing) sessions.Issue(http);
        return await next(context);
    }
}
