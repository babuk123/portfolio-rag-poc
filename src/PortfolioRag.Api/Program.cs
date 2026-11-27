using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PortfolioRag.Api.Data;
using PortfolioRag.Api.Options;
using PortfolioRag.Api.Services;

// Linux App Service has no regional culture set, so "C" would print a generic currency sign. Use US formatting everywhere.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration (values come from appsettings + user-secrets; see README) ----
builder.Services.Configure<AzureOpenAIOptions>(builder.Configuration.GetSection(AzureOpenAIOptions.Section));
builder.Services.Configure<AzureSearchOptions>(builder.Configuration.GetSection(AzureSearchOptions.Section));
builder.Services.Configure<BlobStorageOptions>(builder.Configuration.GetSection(BlobStorageOptions.Section));
builder.Services.Configure<MarketDataOptions>(builder.Configuration.GetSection(MarketDataOptions.Section));

// ---- Data ----
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Sql")));

// ---- Services ----
// SDK clients are thread-safe and expensive to create -> Singleton.
builder.Services.AddSingleton<OpenAIService>();
builder.Services.AddSingleton<SearchService>();
builder.Services.AddSingleton<BlobStorageService>();
builder.Services.AddSingleton<PdfService>();

// Live market data: named HttpClients from IHttpClientFactory (pooled handlers, no socket exhaustion,
// DNS changes picked up) with short timeouts so a slow provider can't hang an advisor's request.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(MarketDataService.FinnhubClient, c =>
{
    c.BaseAddress = new Uri("https://finnhub.io/api/v1/");
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient(MarketDataService.TwelveDataClient, c =>
{
    c.BaseAddress = new Uri("https://api.twelvedata.com/");
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<MarketDataService>();
// Anything that uses the DbContext -> Scoped (one per HTTP request).
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<RagChatService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddCors(o => o.AddPolicy("ui", p => p
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// ---- One-time setup: database + seed, blob container, search index ----
using (var scope = app.Services.CreateScope())
{
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();   // POC only. Use EF migrations in real projects.
    SeedData.Seed(db);

    try { await scope.ServiceProvider.GetRequiredService<BlobStorageService>().EnsureContainerAsync(); }
    catch (Exception ex) { log.LogWarning(ex, "Blob Storage not ready. Check BlobStorage settings."); }

    try { await scope.ServiceProvider.GetRequiredService<SearchService>().EnsureIndexAsync(); }
    catch (Exception ex) { log.LogWarning(ex, "Azure AI Search not ready. Check AzureSearch settings."); }
}

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();   // JSON spec at /openapi/v1.json

app.UseHttpsRedirection();

// In Azure the Angular build is copied into wwwroot, so one App Service serves UI + API (same origin, no CORS).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("ui");   // only needed locally (ng serve on :4200)
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapFallbackToFile("index.html");   // Angular routes

app.Run();
