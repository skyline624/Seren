using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Seren.Server.Api.IntegrationTests;

/// <summary>
/// Base <see cref="WebApplicationFactory{TEntryPoint}"/> that isolates each
/// test class with its own temporary JSON character store. Prevents cross-
/// fixture contention and keeps a clean slate per test run.
/// </summary>
public class SerenTestFactory : WebApplicationFactory<Program>
{
    private readonly string _charactersPath = Path.Combine(
        Path.GetTempPath(), $"seren_test_chars_{Guid.NewGuid():N}.json");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Seren:Characters:StorePath", _charactersPath);
        builder.UseSetting("Seren:WebSocket:ReadTimeoutSeconds", "0");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { File.Delete(_charactersPath); }
            catch { /* best-effort cleanup */ }
        }
    }
}
