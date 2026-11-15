using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SolarInsight.Api;
using SolarInsight.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var runtime = new RuntimeOptions(config.GetValue("Solar:Demo", false),
    Path.GetFullPath(config["Solar:State"] ?? "state"), config.GetValue("Solar:AllowHttp", false));
Directory.CreateDirectory(runtime.State);
var keyDirectory = Directory.CreateDirectory(Path.GetFullPath(config["Solar:Keys"] ??
    Path.Combine(runtime.State, "keys")));
var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("Solar") ??
    throw new InvalidOperationException("Datenbankverbindung fehlt."));
if (config["Solar:DatabasePasswordFile"] is { } dbSecret)
    connection.Password = File.ReadAllText(dbSecret).TrimEnd('\r', '\n');
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 32_768);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Services.AddSingleton(runtime);
builder.Services.AddSingleton<LocalAccess>();
builder.Services.AddSingleton<Visitor>();
builder.Services.AddSingleton<SyntheticProvider>();
builder.Services.AddSingleton(new SungrowDiscovery(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
    { Timeout = Timeout.InfiniteTimeSpan }));
builder.Services.AddSingleton<LiveProvider>();
builder.Services.AddSingleton<SungrowSetup>();
builder.Services.AddDbContext<Store>(o => o.UseNpgsql(connection.ConnectionString));
builder.Services.AddScoped<Queries>();
builder.Services.AddSingleton<ImportCancellation>();
builder.Services.AddSingleton<OverviewActivity>();
builder.Services.AddSingleton<LiveMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveMonitor>());
builder.Services.AddSingleton<IHistorySource>(sp => sp.GetRequiredService<LiveProvider>());
builder.Services.AddSingleton<HistoryWorker>();
builder.Services.AddSingleton<IDeviceCounterSource>(sp => sp.GetRequiredService<LiveProvider>());
builder.Services.AddSingleton<BatteryHistory>();
builder.Services.AddSingleton<LanAccess>();
builder.Services.AddHostedService<ImportWorker>();
builder.Services.AddSingleton<Jobs>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Jobs>());
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddAuthorization();
builder.Services.AddDataProtection().SetApplicationName("SolarInsight").PersistKeysToFileSystem(keyDirectory);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "solar_session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = runtime.AllowHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.Events.OnValidatePrincipal = c =>
    {
        if (c.Principal?.FindFirstValue("stamp") != c.HttpContext.RequestServices.GetRequiredService<LocalAccess>().Stamp)
            c.RejectPrincipal();
        return Task.CompletedTask;
    };
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = runtime.AllowHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
IPAddress? proxy = null;
if (config["Solar:TrustedProxy"] is { Length: > 0 } trustedProxy && !IPAddress.TryParse(trustedProxy, out proxy))
    throw new InvalidOperationException("Solar:TrustedProxy muss eine einzelne IP-Adresse sein.");
if (proxy is not null)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
        o.KnownProxies.Add(proxy);
    });
}
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    void Policy(string name, int count, TimeSpan window) => o.AddPolicy(name, c =>
        RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new() { PermitLimit = count, Window = window }));
    // One dashboard view issues about ten requests; 600 per minute still bounds a misbehaving client.
    Policy("api", 600, TimeSpan.FromMinutes(1));
    Policy("login", 8, TimeSpan.FromMinutes(5));
    Policy("simulate", 12, TimeSpan.FromMinutes(1));
});

var app = builder.Build();
var lan = app.Services.GetRequiredService<LanAccess>();
app.Services.GetRequiredService<LocalAccess>().Initialize(config["Solar:SetupPasswordFile"]);
using (var scope = app.Services.CreateScope())
    await Importer.Initialize(scope.ServiceProvider.GetRequiredService<Store>(), runtime.Demo, app.Lifetime.ApplicationStopping);

if (proxy is not null) app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers.ContentSecurityPolicy = "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'";
    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.Headers.CacheControl = "no-store";
    if (!runtime.AllowHttp && !ctx.Request.IsHttps && ctx.Request.Path.StartsWithSegments("/api"))
    {
        await Results.Problem(statusCode: 400, detail: "HTTPS erforderlich.").ExecuteAsync(ctx);
        return;
    }
    try { await next(ctx); }
    catch (AntiforgeryValidationException)
    {
        await Results.Problem(statusCode: 400, detail: "Sitzungsschutz abgelaufen. Seite neu laden.").ExecuteAsync(ctx);
    }
    catch (ProviderFailure ex)
    {
        string status = ex.HttpStatus is { } http ? $" · HTTP {http}" : "";
        string code = ex.ResultCode is { } result ? $" · Sungrow-Code {result}" : "";
        if (ctx.Request.Path == "/api/v1/provider/authorize")
            app.Logger.LogWarning("Sungrow-OAuth-Codeaustausch fehlgeschlagen. Kategorie {Category}, HTTP {HttpStatus}, Ergebniscode {ResultCode}, Antwortstruktur {ResponseShape}",
                ex.Category, ex.HttpStatus, ex.ResultCode, ex.ResponseShape);
        await Results.Problem(statusCode: 502,
            detail: "Sungrow-Lesezugriff fehlgeschlagen: " + ex.Category + status + code).ExecuteAsync(ctx);
    }
    catch (TimeoutException)
    {
        await Results.Problem(statusCode: 504, detail: "Zeitüberschreitung beim Providerzugriff.").ExecuteAsync(ctx);
    }
    catch (HttpRequestException)
    {
        await Results.Problem(statusCode: 502, detail: "Netzwerkfehler beim Providerzugriff.").ExecuteAsync(ctx);
    }
    catch (ArgumentException ex)
    {
        await Results.Problem(statusCode: 400, detail: ex.Message).ExecuteAsync(ctx);
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (Store db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
// The API description lists every endpoint; only expose it where no private data is served.
if (runtime.Demo || app.Environment.IsDevelopment()) app.MapOpenApi();
var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("api");
auth.MapGet("/session", (HttpContext ctx, IAntiforgery csrf) =>
    new { demo = runtime.Demo, authenticated = ctx.User.Identity?.IsAuthenticated ?? false,
        csrf = csrf.GetAndStoreTokens(ctx).RequestToken });
auth.MapPost("/login", async (Login input, HttpContext ctx, IAntiforgery csrf, LocalAccess access) =>
{
    if (runtime.Demo) return Results.StatusCode(403);
    await csrf.ValidateRequestAsync(ctx);
    if (input.Password is null || !access.Verify(input.Password)) return Results.Unauthorized();
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(
        [new(ClaimTypes.Name, "owner"), new("stamp", access.Stamp)], CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.NoContent();
}).RequireRateLimiting("login");
auth.MapPost("/logout", async (HttpContext ctx, IAntiforgery csrf) =>
{
    await csrf.ValidateRequestAsync(ctx);
    await ctx.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization();
auth.MapPut("/password", async (PasswordChange change, HttpContext ctx, IAntiforgery csrf, LocalAccess access) =>
{
    if (runtime.Demo) return Results.StatusCode(403);
    await csrf.ValidateRequestAsync(ctx);
    if (change.Current is null || change.Next is null) throw new ArgumentException("Passwort fehlt.");
    access.Change(change.Current, change.Next);
    await ctx.SignOutAsync();
    return Results.NoContent();
}).RequireAuthorization().RequireRateLimiting("login");

var api = app.MapGroup("/api/v1").RequireRateLimiting("api");
if (!runtime.Demo) api.RequireAuthorization();
api.AddEndpointFilter(async (invocation, next) =>
{
    var ctx = invocation.HttpContext;
    if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
        await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx);
    return await next(invocation);
});
Endpoints.Map(api);
AnalyticsEndpoints.Map(api);
ProviderEndpoints.Map(api);
LiveEndpoints.Map(api);
app.MapGet("/api/v1/lan/now", async (HttpContext ctx, Queries queries, Store db, LiveMonitor monitor, CancellationToken ct) =>
{
    if (runtime.Demo || !lan.Enabled) return Results.NotFound();
    if (!lan.Allows(ctx.Connection.RemoteIpAddress, ctx.Request.Headers.Authorization)) return Results.Unauthorized();
    var plant = await queries.Plant(ct);
    var state = await monitor.State(db, plant.Id, await LiveMonitor.Mapping(db, plant.Id, ct), ct);
    return Results.Ok(state);
}).RequireRateLimiting("api");
app.Map("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
app.Run();

record Login(string Password);
record PasswordChange(string Current, string Next);
public partial class Program;

