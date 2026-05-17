using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LightRAGNet.Server.Tests;

public class ServerHostSmokeTests
{
    [Fact]
    public void ServerHost_CanBeCreated()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                        ["LLM:ApiKey"] = "test-key",
                        ["Embedding:ApiKey"] = "test-key",
                        ["Rerank:ApiKey"] = "test-key",
                        ["Neo4j:Uri"] = "neo4j://localhost:7687",
                        ["Neo4j:User"] = "neo4j",
                        ["Neo4j:Password"] = "test-password",
                        ["LightRAG:WorkingDir"] = "rag_storage_test"
                    });
                });
            });

        using var client = factory.CreateClient();

        client.Should().NotBeNull();
    }
}
