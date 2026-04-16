using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Seren.Application.Abstractions;
using Seren.Application.Behaviors;
using Seren.Application.DependencyInjection;
using Seren.Infrastructure.Authentication;
using Seren.Infrastructure.Cors;
using Seren.Infrastructure.DependencyInjection;
using Seren.Infrastructure.RateLimiting;
using Seren.Infrastructure.Realtime;
using Seren.Infrastructure.Security;
using Seren.Server.Api.Endpoints;
using Seren.Server.Api.Security;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Serilog (structured logs).
// ---------------------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// ---------------------------------------------------------------------------
// OpenTelemetry (traces). Emits OTLP; local collector optional.
// ---------------------------------------------------------------------------
var otelSection = builder.Configuration.GetSection("OpenTelemetry");
var serviceName = otelSection.GetValue<string>("ServiceName") ?? "seren.hub";
var otlpEndpoint = otelSection.GetValue<string>("OtlpEndpoint");

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(serviceName: serviceName, serviceVersion: "0.1.0"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("seren.*")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    });

// ---------------------------------------------------------------------------
// Mediator (source-generated) + pipeline behaviors.
// ---------------------------------------------------------------------------
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
});

// Pipeline behaviors share the Mediator lifetime (Scoped) because ValidationBehavior
// depends on scoped FluentValidation validators. A singleton behavior would capture
// scoped validators and produce a captive-dependency bug.
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// ---------------------------------------------------------------------------
// Application + Infrastructure layers.
// ---------------------------------------------------------------------------
builder.Services.AddSerenApplication();
builder.Services.AddSerenInfrastructure(builder.Configuration);

// ---------------------------------------------------------------------------
// Authentication + Authorization.
// ---------------------------------------------------------------------------
var authOptions = new AuthOptions();
builder.Configuration.GetSection(AuthOptions.SectionName).Bind(authOptions);

if (authOptions.RequireAuthentication && !string.IsNullOrWhiteSpace(authOptions.JwtSecret))
{
    // Production mode: require valid JWT on protected endpoints.
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = authOptions.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(authOptions.JwtSecret)),
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            // Allow JWT in query string for WebSocket connections (browsers cannot
            // set headers during the WebSocket upgrade handshake) AND enforce the
            // revocation store so that logged-out tokens are rejected.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (string.IsNullOrEmpty(context.Token)
                        && context.Request.Query.TryGetValue("access_token", out var token))
                    {
                        context.Token = token;
                    }

                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                    if (string.IsNullOrWhiteSpace(jti))
                    {
                        return;
                    }

                    var store = context.HttpContext.RequestServices
                        .GetRequiredService<ITokenRevocationStore>();
                    var isRevoked = await store.IsRevokedAsync(jti, context.HttpContext.RequestAborted)
                        .ConfigureAwait(false);
                    if (isRevoked)
                    {
                        context.Fail("Token has been revoked");
                    }
                },
            };
        });
}
else
{
    // Dev mode: no authentication required. Register a no-op authentication scheme
    // so that UseAuthentication()/UseAuthorization() don't throw at runtime.
    builder.Services.AddAuthentication();
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(SerenPolicies.AdminOnly, policy =>
        policy.RequireRole(SerenRoles.Admin))
    .AddPolicy(SerenPolicies.RequireAuth, policy =>
        policy.RequireAuthenticatedUser());

// ---------------------------------------------------------------------------
// Health checks.
// ---------------------------------------------------------------------------
builder.Services
    .AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy("Seren hub is ready."),
        tags: ["ready"]);

// ---------------------------------------------------------------------------
// Build + pipeline.
// ---------------------------------------------------------------------------
var app = builder.Build();

// ── Auto-create database on startup ──────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Seren.Infrastructure.Persistence.SerenDbContext>();
    await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
}

app.UseSerilogRequestLogging();

// ── Security headers (first so even error pages carry them) ───────────────
app.UseSerenSecurityHeaders();

// ── CORS (before auth) ────────────────────────────────────────────────────
app.UseCors(CorsOptions.PolicyName);

// ── Rate limiting (after CORS, before auth) ───────────────────────────────
app.UseSerenRateLimiting();

// ── Authentication / Authorization ─────────────────────────────────────────
// When RequireAuthentication is false (dev mode), endpoints remain open.
// When true, [Authorize] attributes and policies enforce authentication.
app.UseAuthentication();
app.UseAuthorization();

var hubOptions = app.Services.GetRequiredService<IOptions<SerenHubOptions>>().Value;

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(hubOptions.KeepAliveIntervalSeconds),
});

app.MapGet("/", () => Results.Text(
    "Seren Hub — Phase 1 online. WebSocket endpoint: " + hubOptions.Path));

app.MapSerenHealthChecks();
app.MapAuthEndpoints();
app.MapCharacterEndpoints();
app.MapModelEndpoints();
app.MapSerenWebSocketEndpoint(authOptions.RequireAuthentication);

Log.Information("Seren hub starting on port configured by ASPNETCORE_URLS, WS at {Path}, Auth={Auth}",
    hubOptions.Path, authOptions.RequireAuthentication ? "required" : "open");

await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Exposed so that <c>WebApplicationFactory&lt;Program&gt;</c> can boot the app
/// from integration tests in <c>Seren.Server.Api.IntegrationTests</c>.
/// </summary>
public partial class Program;
