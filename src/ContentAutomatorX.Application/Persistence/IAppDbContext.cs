using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Persistence;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Source> Sources { get; }
    DbSet<ContentItem> ContentItems { get; }
    DbSet<Recipe> Recipes { get; }
    DbSet<Draft> Drafts { get; }
    DbSet<PipelineRun> PipelineRuns { get; }
    DbSet<PromptTemplate> PromptTemplates { get; }
    DbSet<Platform> Platforms { get; }
    DbSet<Post> Posts { get; }
    DbSet<IssueSection> IssueSections { get; }
    DbSet<TenantLlmSetting> TenantLlmSettings { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
