using Microsoft.EntityFrameworkCore;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Hubs;
using LightRAGNet.Hosting;
using Scalar.AspNetCore;
using Microsoft.Extensions.FileProviders;
using System.Reflection;
using LightRAGNet.Core.Utils;
using LightRAGNet.Server.Services;
using LightRAGNet.Server.Services.CacheManagement;
using LightRAGNet.Server.Services.DocumentArtifacts;
using LightRAGNet.Server.Services.DocumentConversion;
using LightRAGNet.Server.Services.SystemHealth;
using LightRAGNet.Server.Services.SystemHealth.Checks;
using LightRAGNet.Core.Interfaces;
using LightRAGNet.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize enums as strings instead of numbers
        options.JsonSerializerOptions.Encoder = LightRAGJsonOptions.HumanReadable.Encoder;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Configure CORS (support SignalR)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "https://localhost:7190",
                "http://localhost:5241",
                "https://localhost:7291",
                "http://localhost:5261",
                "http://localhost:5173",
                "http://127.0.0.1:5173")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Relative path, convert to absolute path based on application runtime path
var baseDir = AppDomain.CurrentDomain.BaseDirectory;

// Configure EFCore and SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=markdown_documents.db";
    
// If relative path, convert to absolute path based on application runtime path
if (connectionString.StartsWith("Data Source="))
{
    var dbPath = connectionString.Substring("Data Source=".Length);
    if (!Path.IsPathRooted(dbPath))
    {
        dbPath = Path.Combine(baseDir, dbPath);
        connectionString = $"Data Source={dbPath}";
    }
}

builder.Services.AddDbContext<AppDbContext>(
    options => options.UseSqlite(connectionString),
    optionsLifetime: ServiceLifetime.Singleton);
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.Configure<DocumentArtifactStoreOptions>(options =>
{
    var workingDir = builder.Configuration["LightRAG:WorkingDir"] ?? "rag_storage";
    if (!Path.IsPathRooted(workingDir))
    {
        workingDir = Path.Combine(baseDir, workingDir);
    }

    options.RootPath = workingDir;
});
builder.Services.AddScoped<IDocumentArtifactStore, FileSystemDocumentArtifactStore>();
builder.Services.AddScoped<IDocumentMarkdownConverter, ManagedCodeDocumentMarkdownConverter>();
builder.Services.AddSingleton<DocumentConversionCoordinator>();
builder.Services.AddScoped<DocumentConversionProcessor>();
builder.Services.AddHostedService<DocumentConversionWorker>();

builder.Services.AddScoped<MarkdownDocumentDeletionService>();
builder.Services.AddScoped<DocumentIntakeService>();
builder.Services.AddScoped<IRagExternalStorageCleaner, RagExternalStorageCleaner>();

// Register SignalR (for real-time task status updates)
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Encoder = LightRAGJsonOptions.HumanReadable.Encoder;
    });

// Register LightRAG services (including task queue services)
builder.Services.AddLightRAG(builder.Configuration);
builder.Services.AddSingleton(sp => new CacheEntryInspector(
    sp.GetRequiredKeyedService<IKVStore>(KVContracts.LLMCache)));
builder.Services.AddSingleton<CacheClearPlanner>();
builder.Services.AddSingleton<CacheManagementService>();

builder.Services.Configure<SystemHealthOptions>(builder.Configuration.GetSection("SystemHealth"));
builder.Services.AddScoped<SystemHealthService>();
builder.Services.AddScoped<ISystemHealthCheck, ServerApiHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, SqliteHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, WorkingDirHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, QdrantHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, Neo4jHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, LlmConfigHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, EmbeddingConfigHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, RerankConfigHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, RagTaskQueueHealthCheck>();
builder.Services.AddScoped<ISystemHealthCheck, DocumentConversionQueueHealthCheck>();

// Register MediatR (for event handlers in Server project)
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

var app = builder.Build();

// Apply database migrations
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Automatically apply pending migrations
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
// Configure OpenAPI
app.MapOpenApi();

// Configure Scalar API documentation and testing interface (only enabled in development environment)
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("LightRAGNet API Documentation")
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
            .WithTheme(ScalarTheme.BluePlanet);
    });
}

app.UseHttpsRedirection();
app.UseCors();
// Configure static file service for serving uploaded Markdown files
var uploadsPath = Path.Combine(baseDir, "Uploads");

if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseAuthorization();

app.MapControllers();

// Map SignalR Hub
app.MapHub<RagTaskHub>("/hubs/ragtask");

app.Run();

public partial class Program;
