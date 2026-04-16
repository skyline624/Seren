using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Seren.Infrastructure.Cors;

/// <summary>
/// Extension methods to configure CORS for Seren.
/// </summary>
public static class CorsServiceExtensions
{
    /// <summary>
    /// Adds CORS services with a policy configured from <c>Seren:Cors</c>.
    /// When <c>AllowedOrigins</c> is empty, only same-origin requests are allowed.
    /// </summary>
    public static IServiceCollection AddSerenCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var corsOptions = new CorsOptions();
        configuration.GetSection(CorsOptions.SectionName).Bind(corsOptions);

        services.AddCors(options =>
        {
            options.AddPolicy(CorsOptions.PolicyName, policy =>
            {
                if (corsOptions.AllowedOrigins.Length == 0)
                {
                    // In development with no origins configured, allow all
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                }
                else
                {
                    policy.WithOrigins(corsOptions.AllowedOrigins)
                          .WithMethods(corsOptions.AllowedMethods)
                          .WithHeaders(corsOptions.AllowedHeaders);

                    if (corsOptions.AllowCredentials)
                    {
                        policy.AllowCredentials();
                    }
                    else
                    {
                        policy.DisallowCredentials();
                    }
                }
            });
        });

        return services;
    }
}
