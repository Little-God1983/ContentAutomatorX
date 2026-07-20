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
    public DbSet<IssueSection> IssueSections => Set<IssueSection>();
    public DbSet<TenantLlmSetting> TenantLlmSettings => Set<TenantLlmSetting>();
    public DbSet<IssueChatMessage> IssueChatMessages => Set<IssueChatMessage>();
    public DbSet<IssueSectionProposal> IssueSectionProposals => Set<IssueSectionProposal>();
    public DbSet<IssueRevision> IssueRevisions => Set<IssueRevision>();

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
        b.Entity<IssueSection>().HasIndex(s => new { s.PostId, s.Position });
        b.Entity<IssueSection>()
            .HasOne<Post>().WithMany().HasForeignKey(s => s.PostId).OnDelete(DeleteBehavior.Cascade);
        // Unique so "at most one row per tenant" is enforced by the database, not
        // by hoping every writer goes through SaveAsync's upsert. No FK to Tenant:
        // no tenant-owned entity here declares one (see Platform, Recipe, Source).
        b.Entity<TenantLlmSetting>().HasIndex(s => s.TenantId).IsUnique();
        b.Entity<IssueChatMessage>().HasIndex(m => m.PostId);
        b.Entity<IssueChatMessage>()
            .HasOne<Post>().WithMany().HasForeignKey(m => m.PostId).OnDelete(DeleteBehavior.Cascade);
        // Unique so "at most one pending proposal per section" is enforced by the database, not by
        // hoping every writer remembers to delete the previous one first.
        b.Entity<IssueSectionProposal>().HasIndex(p => p.SectionId).IsUnique();
        b.Entity<IssueSectionProposal>().HasIndex(p => p.PostId);
        b.Entity<IssueSectionProposal>()
            .HasOne<Post>().WithMany().HasForeignKey(p => p.PostId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<IssueRevision>().HasIndex(r => new { r.PostId, r.Stack, r.Ordinal });
        b.Entity<IssueRevision>()
            .HasOne<Post>().WithMany().HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);
    }
}
