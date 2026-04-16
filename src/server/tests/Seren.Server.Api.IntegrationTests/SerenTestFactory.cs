using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Seren.Server.Api.IntegrationTests;

/// <summary>
/// Base <see cref="WebApplicationFactory{TEntryPoint}"/> that isolates each
/// test class with its own temporary SQLite database file via connection
/// string override. Prevents cross-fixture file locking.
/// </summary>
public class SerenTestFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"seren_test_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:SerenDb", $"Data Source={_dbPath}");
        builder.UseSetting("Seren:WebSocket:ReadTimeoutSeconds", "0");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { File.Delete(_dbPath); }
            catch { /* best-effort cleanup */ }
        }
    }
}
