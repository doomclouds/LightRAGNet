using LightRAGNet.Web;
using LightRAGNet.Web.Components;
using LightRAGNet.Web.Services;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure detailed error information (development environment)
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents(options =>
        {
            options.DetailedErrors = true;
        });
}
else
{
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
}

// Add MudBlazor services
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = true;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 2000; // Display time 2 seconds
    config.SnackbarConfiguration.HideTransitionDuration = 200; // Hide animation time 200 milliseconds
    config.SnackbarConfiguration.ShowTransitionDuration = 200; // Show animation time 200 milliseconds
    config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
});
builder.Services.AddMudMarkdownServices();

// Add HTTP client factory
var baseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5261";
builder.Services.AddHttpClient<ApiClient>("MarkdownApi", client =>
{
    client.BaseAddress = new Uri(baseUrl);
});

// Add task status notification service (singleton, globally shared)
builder.Services.AddSingleton<RagTaskNotificationService>();

// Add chat history service (singleton, in-memory storage)
builder.Services.AddSingleton<ChatHistoryService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();