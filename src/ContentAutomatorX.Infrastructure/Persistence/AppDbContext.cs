using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<Post> Posts => Set<Post>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>().HasIndex(t => t.Slug).IsUnique();
        b.Entity<ContentItem>().HasIndex(i => new { i.SourceId, i.ExternalId }).IsUnique();
        b.Entity<ContentItem>().HasIndex(i => new { i.TenantId, i.NormalizedUrl }).IsUnique()
            .HasFilter("\"NormalizedUrl\" IS NOT NULL");
        b.Entity<ContentItem>().Property(i => i.Status).HasConversion<string>();
        b.Entity<Draft>().Property(d => d.Status).HasConversion<string>();
        b.Entity<PipelineRun>().Property(r => r.Status).HasConversion<string>();
        b.Entity<Post>().Property(p => p.Status).HasConversion<string>();
        b.Entity<Post>().HasIndex(p => new { p.TenantId, p.Status });
        b.Entity<Platform>().HasIndex(p => new { p.TenantId, p.Type }).IsUnique();
    }
}
