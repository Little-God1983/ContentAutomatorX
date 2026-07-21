using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Delivery;
using ContentAutomatorX.Infrastructure.Llm;
using ContentAutomatorX.Infrastructure.Newsletter;
using ContentAutomatorX.Infrastructure.Persistence;
using ContentAutomatorX.Infrastructure.Platforms;
using ContentAutomatorX.Infrastructure.Security;
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
    .WriteTo.File(Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "contentx-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

// --- persistence ---
var dbPath = builder.Configuration["Database:Path"];
if (string.IsNullOrWhiteSpace(dbPath))
    dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "contentx.db");
else if (!Path.IsPathRooted(dbPath))
    dbPath = Path.Combine(builder.Environment.ContentRootPath, dbPath);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

// --- source connectors (HTTP + retry/backoff) ---
builder.Services.AddHttpClient<RssConnector>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<RedditConnector>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<WebsiteConnector>().AddStandardResilienceHandler();
builder.Services.AddTransient<ISourceConnector>(sp => sp.GetRequiredService<RssConnector>());
builder.Services.AddTransient<ISourceConnector>(sp => sp.GetRequiredService<RedditConnector>());
builder.Services.AddTransient<ISourceConnector>(sp => sp.GetRequiredService<WebsiteConnector>());
builder.Services.AddTransient<ISourceConnector, LlmResearchConnector>(); // no HttpClient — rides ILlmBackend

// --- MailerLite connector (HTTP + retry/backoff) ---
builder.Services.AddHttpClient<MailerLiteClient>().AddStandardResilienceHandler();
builder.Services.AddTransient<IMailerLiteClient>(sp => sp.GetRequiredService<MailerLiteClient>());

// --- YouTube thumbnail resolver (HTTP HEAD probe) ---
builder.Services.AddHttpClient<IYouTubeThumbnailResolver, YouTubeThumbnailResolver>(c =>
    c.Timeout = TimeSpan.FromSeconds(5));

// --- LLM backend ---
var claudeOptions = new ClaudeCliOptions();
builder.Configuration.GetSection("Claude").Bind(claudeOptions);
if (string.IsNullOrWhiteSpace(claudeOptions.Model)) claudeOptions.Model = null;
builder.Services.AddSingleton(claudeOptions);
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<ILlmBackend, ClaudeCliBackend>();

// appsettings values become the fallback for tenants that have not chosen.
// Wrapped in LlmFallbackSettings rather than registered as a bare LlmSettings: a
// component injecting LlmSettings would otherwise receive this global default in
// place of a tenant's resolved settings and work — wrongly, for every tenant.
// LlmSettings itself carries no Claude types, so Application never sees ClaudeCliOptions.
builder.Services.AddSingleton(new LlmFallbackSettings(
    LlmSettings.From(claudeOptions.Model, claudeOptions.Effort)));
// Scoped, not singleton: every consumer (IssueComposerService, PostService,
// GenerationPipeline, LlmResearchConnector) is scoped or transient, so it can
// take IAppDbContext directly — no IServiceScopeFactory dance needed.
builder.Services.AddScoped<ILlmSettingsProvider, LlmSettingsService>();
if (OperatingSystem.IsWindows())
    builder.Services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
else
    throw new PlatformNotSupportedException(
        "Secrets storage currently requires Windows (DPAPI). A server deployment swaps in a different ICredentialStore.");

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
builder.Services.AddScoped<PlatformService>();
builder.Services.AddScoped<PostService>();
builder.Services.AddScoped<NewsletterTemplateService>();
builder.Services.AddScoped<IssueHistoryService>();
builder.Services.AddScoped<IssueComposerService>();
builder.Services.AddScoped<IssueChatService>();
builder.Services.AddScoped<PostSyncService>();
builder.Services.AddScoped<ContentAutomatorX.Web.Services.ITenantIdStore,
    ContentAutomatorX.Web.Services.ProtectedLocalStorageTenantIdStore>();
builder.Services.AddScoped<ContentAutomatorX.Web.Services.TenantContext>();

// --- scheduler ---
builder.Services.AddHostedService<SchedulerService>();
builder.Services.AddHostedService<PlatformSyncJob>();
builder.Services.AddHostedService<ChatRetentionJob>();

// --- UI + MCP ---
builder.Services.AddMudServices();
// MaximumReceiveMessageSize defaults to 32KB. A tenant's own hand-written newsletter template
// (TemplateValidator.MaxBytes allows up to 512KB) is edited live in a plain <textarea> whose
// *entire* value round-trips to the server on every keystroke — Blazor Server sends the whole
// bound string, not a diff. The 23KB reference template disconnected the circuit on the first
// keystroke, and the failure is silent: no toast, no visible reconnect, the textarea keeps
// accepting input while the preview and error list quietly stop updating, so Save stays enabled
// after a real typo with nothing on screen to explain why.
//
// 23KB tripping a 32KB limit is not explained by JSON escaping — that measures at 1.03x on this
// file. The likely cause is Blazor's binding protocol carrying the value more than once per event
// dispatch (EventFieldInfo alongside ChangeEventArgs.Value). Sizing off that: a max-size 512KB
// template projects to roughly 1.0-1.1MB on the wire, so 2MB is about 2x headroom. Not measured
// at the 512KB ceiling — if that is ever raised, re-measure rather than scaling this figure.
//
// Trade-off, accepted deliberately: this is a global hub option, so it applies to every circuit
// in the app, not just the template editor — Blazor Server cannot scope it per component. A
// larger ceiling means a larger memory commitment per malicious message. This app binds to
// localhost by default; revisit if it is ever exposed publicly.
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 2 * 1024 * 1024);
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

var app = builder.Build();

// migrate + seed system default templates
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    await NormalizedUrlBackfill.RunAsync(db);
    foreach (var kind in DraftKinds.All)
        if (!db.PromptTemplates.Any(p => p.TenantId == null && p.Kind == kind))
            db.PromptTemplates.Add(new PromptTemplate { TenantId = null, Kind = kind, Template = DefaultTemplates.GetFor(kind) });
    foreach (var stale in db.PipelineRuns.Where(r => r.Status == RunStatus.Running))
    {
        stale.Status = RunStatus.Failed;
        stale.FinishedAt = DateTimeOffset.UtcNow;
    }
    db.SaveChanges();
}

app.MapStaticAssets();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapMcp("/mcp");

app.Run();
