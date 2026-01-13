using Microsoft.EntityFrameworkCore;
using LightRAGNet.Server.Data;
using LightRAGNet.Server.Hubs;
using LightRAGNet.Hosting;
using Scalar.AspNetCore;
using Microsoft.Extensions.FileProviders;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize enums as strings instead of numbers
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Configure CORS (support SignalR)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://localhost:7190", "http://localhost:5092", "https://localhost:7291", "http://localhost:5261")
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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// Register SignalR (for real-time task status updates)
builder.Services.AddSignalR();

// Register LightRAG services (including task queue services)
builder.Services.AddLightRAG(builder.Configuration);

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