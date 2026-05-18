using System.Data;
using LightRAGNet.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LightRAGNet.Server.Tests;

internal sealed class LightRagServerFactory : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>? configureTestServices;
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "LightRAGNet.Server.Tests",
        Guid.NewGuid().ToString("N"));

    public LightRagServerFactory(Action<IServiceCollection>? configureTestServices = null)
    {
        this.configureTestServices = configureTestServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LLM:ApiKey"] = "test-key",
                ["Embedding:ApiKey"] = "test-key",
                ["Rerank:ApiKey"] = "test-key",
                ["Neo4j:Uri"] = "neo4j://localhost:7687",
                ["Neo4j:User"] = "neo4j",
                ["Neo4j:Password"] = "test-password",
                ["LightRAG:WorkingDir"] = workingDirectory
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            configureTestServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            connection.Dispose();
            TryDeleteWorkingDirectory();
        }
    }

    private void TryDeleteWorkingDirectory()
    {
        try
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
