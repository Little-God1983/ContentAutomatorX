using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Delivery;
using ContentAutomatorX.Infrastructure.Llm;
using ContentAutomatorX.Infrastructure.Persistence;
using ContentAutomatorX.Infrastructure.Sources;
using ContentAutomatorX.Web.Components;
using ContentAutomatorX.Web.Jobs;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/contentx-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

// --- persistence ---
var dbPath = builder.Configuration["Database:Path"];
if (string.IsNullOrWhiteSpace(dbPath))
    dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "contentx.db");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

// --- source connectors (HTTP + retry/backoff) ---
builder.Services.AddHttpClient<RssConnector>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<RedditConnector>().AddStandardResilienceHandler();
builder.Services.AddTransient<ISourceConnector>(sp => sp.GetRequiredService<RssConnector>());
builder.Services.AddTransient<ISourceConnector>(sp => sp.GetRequiredService<RedditConnector>());

// --- LLM backend ---
var claudeOptions = new ClaudeCliOptions();
builder.Configuration.GetSection("Claude").Bind(claudeOptions);
if (string.IsNullOrWhiteSpace(claudeOptions.Model)) claudeOptions.Model = null;
builder.Services.AddSingleton(claudeOptions);
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<ILlmBackend, ClaudeCliBackend>();

// --- delivery, pipelines, services ---
builder.Services.AddSingleton<IDraftDelivery, FileShareDraftDelivery>();
builder.Services.AddScoped<IngestionPipeline>();
builder.Services.AddScoped<GenerationPipeline>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<SourceService>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<DraftService>();
builder.Services.AddScoped<RunService>();

// --- scheduler ---
builder.Services.AddHostedService<SchedulerService>();

// --- UI + MCP ---
builder.Services.AddMudServices();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

var app = builder.Build();

// migrate + seed system default templates
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    foreach (var kind in DraftKinds.All)
        if (!db.PromptTemplates.Any(p => p.TenantId == null && p.Kind == kind))
            db.PromptTemplates.Add(new PromptTemplate { TenantId = null, Kind = kind, Template = DefaultTemplates.GetFor(kind) });
    db.SaveChanges();
}

app.MapStaticAssets();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapMcp("/mcp");

app.Run();
