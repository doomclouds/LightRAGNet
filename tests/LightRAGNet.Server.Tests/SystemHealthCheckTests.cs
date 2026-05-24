using System.Text.Json;
using FluentAssertions;
using LightRAGNet.Embedding;
using LightRAGNet.LLM;
using LightRAGNet.Models;
using LightRAGNet.Rerank;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Models;
using LightRAGNet.Server.Services;
using LightRAGNet.Server.Services.DocumentArtifacts;
using LightRAGNet.Server.Services.SystemHealth;
using LightRAGNet.Server.Services.SystemHealth.Checks;
using LightRAGNet.Services.TaskQueue;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LightRAGNet.Server.Tests;

public sealed class SystemHealthCheckTests
{
    [Fact]
    public async Task LlmConfigHealthCheck_WhenApiKeyConfigured_ReturnsHealthyWithoutExposingKey()
    {
        var check = new LlmConfigHealthCheck(Options.Create(new DeepSeekOptions
        {
            ApiKey = "secret-llm-key",
            ModelName = "deepseek-test",
            BaseUrl = "https://llm.example.test"
        }));

        var result = await check.CheckAsync(CancellationToken.None);

        result.Id.Should().Be("llm-config");
        result.Name.Should().Be("LLM config");
        result.Category.Should().Be("Providers");
        result.Status.Should().Be(SystemHealthStatus.Healthy);
        result.Evidence.Should().ContainKey("configured").WhoseValue.Should().Be(true);
        result.Evidence.Should().ContainKey("source").WhoseValue.Should().Be("options");
        JsonSerializer.Serialize(result.Evidence).Should().NotContain("secret-llm-key");
    }

    [Fact]
    public async Task LlmConfigHealthCheck_WhenApiKeyMissing_ReturnsUnhealthy()
    {
        var original = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
        try
        {
            var check = new LlmConfigHealthCheck(Options.Create(new DeepSeekOptions { ApiKey = string.Empty }));

            var result = await check.CheckAsync(CancellationToken.None);

            result.Status.Should().Be(SystemHealthStatus.Unhealthy);
            result.Remediation.Should().NotBeNullOrWhiteSpace();
            result.Evidence.Should().ContainKey("configured").WhoseValue.Should().Be(false);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", original);
        }
    }

    [Fact]
    public async Task EmbeddingConfigHealthCheck_WhenDimensionMissing_ReturnsUnhealthy()
    {
        var check = new EmbeddingConfigHealthCheck(Options.Create(new AliyunEmbeddingOptions
        {
            ApiKey = "secret-embedding-key",
            Dimension = 0
        }));

        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Unhealthy);
        result.Evidence.Should().ContainKey("dimension").WhoseValue.Should().Be(0);
        JsonSerializer.Serialize(result.Evidence).Should().NotContain("secret-embedding-key");
    }

    [Fact]
    public async Task RerankConfigHealthCheck_WhenApiKeyMissing_ReturnsDegraded()
    {
        var original = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY");
        Environment.SetEnvironmentVariable("DASHSCOPE_API_KEY", null);
        try
        {
            var check = new RerankConfigHealthCheck(Options.Create(new AliyunRerankOptions { ApiKey = string.Empty }));

            var result = await check.CheckAsync(CancellationToken.None);

            result.Status.Should().Be(SystemHealthStatus.Degraded);
            result.Affects.Should().Contain("Rerank Quality");
            result.Evidence.Should().ContainKey("configured").WhoseValue.Should().Be(false);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DASHSCOPE_API_KEY", original);
        }
    }

    [Fact]
    public async Task WorkingDirHealthCheck_WhenDirectoryWritable_ReturnsHealthy()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "lightragnet-health-tests", Guid.NewGuid().ToString("N"));
        var check = new WorkingDirHealthCheck(
            Options.Create(new DocumentArtifactStoreOptions { RootPath = rootPath }),
            NullLogger<WorkingDirHealthCheck>.Instance);

        try
        {
            var result = await check.CheckAsync(CancellationToken.None);

            result.Status.Should().Be(SystemHealthStatus.Healthy);
            result.Evidence.Should().ContainKey("writable").WhoseValue.Should().Be(true);
            result.Evidence.Should().ContainKey("path").WhoseValue.Should().Be(rootPath);
            Directory.EnumerateFiles(rootPath, ".health-probe-*.tmp").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SqliteHealthCheck_WhenDatabaseConnects_ReturnsHealthy()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        fixture.Context.MarkdownDocuments.Add(new MarkdownDocument
        {
            FileName = "health.md",
            Content = "# health",
            FileSize = 8,
            UploadTime = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync(CancellationToken.None);
        var factory = new CountingDbContextFactory(fixture.Options);
        var check = new SqliteHealthCheck(factory);

        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Healthy);
        result.Evidence.Should().ContainKey("documentCount").WhoseValue.Should().Be(1);
        factory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task RagTaskQueueHealthCheck_WhenOldPendingTaskExists_ReturnsDegraded()
    {
        var store = new FakeRagTaskStateStore([
            new RagTask
            {
                TaskId = "old-pending",
                Status = RagTaskStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddMinutes(-31)
            }
        ]);
        var check = new RagTaskQueueHealthCheck(store);

        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Degraded);
        result.Evidence.Should().ContainKey("pending").WhoseValue.Should().Be(1);
        result.Evidence.Should().ContainKey("staleActive").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task DocumentConversionQueueHealthCheck_WhenOldProcessingConversionExists_ReturnsDegraded()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        fixture.Context.MarkdownDocuments.Add(new MarkdownDocument
        {
            FileName = "old.pdf",
            Content = string.Empty,
            FileSize = 10,
            UploadTime = DateTime.UtcNow.AddMinutes(-45),
            ConversionStatus = DocumentConversionStatus.Processing,
            ConversionStartedAt = DateTime.UtcNow.AddMinutes(-45)
        });
        await fixture.Context.SaveChangesAsync(CancellationToken.None);
        var factory = new CountingDbContextFactory(fixture.Options);
        var check = new DocumentConversionQueueHealthCheck(factory);

        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Degraded);
        result.Message.Should().Contain("stale");
        result.Remediation.Should().Contain("stale");
        result.Evidence.Should().ContainKey("processing").WhoseValue.Should().Be(1);
        result.Evidence.Should().ContainKey("staleActive").WhoseValue.Should().Be(1);
        factory.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task DocumentConversionQueueHealthCheck_WhenFailedConversionExists_ReturnsDegraded()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        fixture.Context.MarkdownDocuments.Add(new MarkdownDocument
        {
            FileName = "failed.pdf",
            Content = string.Empty,
            FileSize = 10,
            UploadTime = DateTime.UtcNow,
            ConversionStatus = DocumentConversionStatus.Failed
        });
        await fixture.Context.SaveChangesAsync(CancellationToken.None);
        var factory = new CountingDbContextFactory(fixture.Options);
        var check = new DocumentConversionQueueHealthCheck(factory);

        var result = await check.CheckAsync(CancellationToken.None);

        result.Status.Should().Be(SystemHealthStatus.Degraded);
        result.Message.Should().Contain("failed");
        result.Remediation.Should().Contain("failed");
        result.Evidence.Should().ContainKey("failed").WhoseValue.Should().Be(1);
        result.Evidence.Should().ContainKey("staleActive").WhoseValue.Should().Be(0);
        factory.CreateCount.Should().Be(1);
    }

    private sealed class FakeRagTaskStateStore(List<RagTask> tasks) : IRagTaskStateStore
    {
        public Task SaveTaskStateAsync(RagTask task, CancellationToken cancellationToken = default)
        {
            tasks.RemoveAll(existing => existing.TaskId == task.TaskId);
            tasks.Add(task);
            return Task.CompletedTask;
        }

        public Task<List<RagTask>> LoadAllTasksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(tasks.ToList());
        }

        public Task<RagTask?> LoadTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(tasks.SingleOrDefault(task => task.TaskId == taskId));
        }

        public Task DeleteTaskStateAsync(string taskId, CancellationToken cancellationToken = default)
        {
            tasks.RemoveAll(task => task.TaskId == taskId);
            return Task.CompletedTask;
        }

        public Task SaveAllTasksAsync(List<RagTask> newTasks, CancellationToken cancellationToken = default)
        {
            tasks.Clear();
            tasks.AddRange(newTasks);
            return Task.CompletedTask;
        }

        public Task ClearAllTasksAsync(CancellationToken cancellationToken = default)
        {
            tasks.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private SqliteFixture(
            SqliteConnection connection,
            DbContextOptions<AppDbContext> options,
            AppDbContext context)
        {
            this.connection = connection;
            Options = options;
            Context = context;
        }

        public DbContextOptions<AppDbContext> Options { get; }

        public AppDbContext Context { get; }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(CancellationToken.None);
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync(CancellationToken.None);
            return new SqliteFixture(connection, options, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class CountingDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public int CreateCount { get; private set; }

        public AppDbContext CreateDbContext()
        {
            CreateCount++;
            return new AppDbContext(options);
        }
    }
}
