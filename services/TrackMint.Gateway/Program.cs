using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TrackMint.Contracts.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TrackMint";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "TrackMint.Client";
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"] ?? "replace-this-in-production-with-a-long-random-key";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey))
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var identity = context.User.Identity?.Name ??
                       context.Connection.RemoteIpAddress?.ToString() ??
                       "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(identity, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

builder.Services.Configure<ServiceUrls>(builder.Configuration.GetSection("ServiceUrls"));

var app = builder.Build();

app.UseAuthentication();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers[Headers.CorrelationId].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
    }

    context.Request.Headers[Headers.CorrelationId] = correlationId;
    context.Response.Headers[Headers.CorrelationId] = correlationId;

    await next();
});

app.Use(async (context, next) =>
{
    if (!RequiresAuthentication(context.Request.Path))
    {
        await next();
        return;
    }

    if (context.User.Identity?.IsAuthenticated == true)
    {
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsJsonAsync(new
    {
        message = "Authentication is required at the gateway."
    });
});
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    service = "TrackMint.Gateway",
    status = "healthy",
    checkedAtUtc = DateTime.UtcNow
}));

app.Map("/{**path}", ProxyAsync);

app.Run();

static async Task ProxyAsync(
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    var route = ResolveRoute(context.Request.Path, configuration);
    if (route is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "No gateway route matched this path.",
            path = context.Request.Path.Value
        }, cancellationToken);
        return;
    }

    var targetUri = BuildTargetUri(context, route.BaseUrl);
    using var requestMessage = CreateProxyRequest(context, targetUri);
    var client = httpClientFactory.CreateClient("gateway-proxy");

    using var responseMessage = await client.SendAsync(
        requestMessage,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken);

    context.Response.StatusCode = (int)responseMessage.StatusCode;

    foreach (var header in responseMessage.Headers)
    {
        context.Response.Headers[header.Key] = header.Value.ToArray();
    }

    foreach (var header in responseMessage.Content.Headers)
    {
        context.Response.Headers[header.Key] = header.Value.ToArray();
    }

    context.Response.Headers.Remove("transfer-encoding");

    await responseMessage.Content.CopyToAsync(context.Response.Body, cancellationToken);
}

static GatewayRoute? ResolveRoute(PathString path, IConfiguration configuration)
{
    var serviceUrls = configuration.GetSection("ServiceUrls").Get<ServiceUrls>() ?? new ServiceUrls();
    var value = path.Value ?? string.Empty;

    if (value.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
    {
        return new GatewayRoute(serviceUrls.AuthService, "auth");
    }

    if (value.StartsWith("/api/accounts", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("/api/categories", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("/api/transactions", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("/api/budgets", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("/api/goals", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("/api/recurring", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("/api/rules", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("/api/finance", StringComparison.OrdinalIgnoreCase))
    {
        return new GatewayRoute(serviceUrls.FinanceService, "finance");
    }

    if (value.StartsWith("/api/dashboard", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("/api/reports", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("/api/forecast", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("/api/insights", StringComparison.OrdinalIgnoreCase))
    {
        return new GatewayRoute(serviceUrls.InsightsService, "insights");
    }

    if (value.StartsWith("/api/notifications", StringComparison.OrdinalIgnoreCase))
    {
        return new GatewayRoute(serviceUrls.NotificationService, "notifications");
    }

    return null;
}

static bool RequiresAuthentication(PathString path)
{
    var value = path.Value ?? string.Empty;

    if (value.Equals("/health", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (value.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (value.EndsWith("/service-info", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
}

static Uri BuildTargetUri(HttpContext context, string baseUrl)
{
    var path = context.Request.Path.Value ?? "/";
    var query = context.Request.QueryString.Value ?? string.Empty;
    return new Uri($"{baseUrl.TrimEnd('/')}{path}{query}");
}

static HttpRequestMessage CreateProxyRequest(HttpContext context, Uri targetUri)
{
    var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

    if (HttpMethods.IsPost(context.Request.Method) ||
        HttpMethods.IsPut(context.Request.Method) ||
        HttpMethods.IsPatch(context.Request.Method))
    {
        requestMessage.Content = new StreamContent(context.Request.Body);
    }

    foreach (var header in context.Request.Headers)
    {
        if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
        {
            requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    requestMessage.Headers.TryAddWithoutValidation("X-Forwarded-For", context.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString());
    requestMessage.Headers.TryAddWithoutValidation("X-Forwarded-Host", context.Request.Host.Value);
    requestMessage.Headers.TryAddWithoutValidation("X-Forwarded-Proto", context.Request.Scheme);

    return requestMessage;
}

internal sealed record GatewayRoute(string BaseUrl, string ServiceName);

internal sealed class ServiceUrls
{
    public string AuthService { get; init; } = "http://localhost:5101";
    public string FinanceService { get; init; } = "http://localhost:5102";
    public string InsightsService { get; init; } = "http://localhost:5103";
    public string NotificationService { get; init; } = "http://localhost:5104";
}
