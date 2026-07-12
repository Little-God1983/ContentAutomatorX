using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Delivery;

namespace ContentAutomatorX.IntegrationTests;

public class FileShareDraftDeliveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"contentx-out-{Guid.NewGuid():N}");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private (Tenant, Draft) Make() =>
        (new Tenant { Name = "T", Slug = "my-channel", OutputFolderPath = _dir },
         new Draft
         {
             TenantId = Guid.NewGuid(), RecipeId = Guid.NewGuid(), Kind = DraftKinds.Newsletter,
             Title = "Big News: AI Everywhere!", Body = "# Big News\nContent here.",
             ModelUsed = "claude-cli", CreatedAt = new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero),
             SourceItemIdsJson = "[\"11111111-1111-1111-1111-111111111111\"]"
         });

    [Fact]
    public async Task Writes_markdown_with_front_matter_into_subfolder()
    {
        var (tenant, draft) = Make();
        var delivery = new FileShareDraftDelivery();

        var path = await delivery.DeliverAsync(tenant, new RecipeOutput { Subfolder = "newsletter" }, draft);

        Assert.True(File.Exists(path));
        Assert.Equal(Path.Combine(_dir, "newsletter", "2026-07-12-newsletter-big-news-ai-everywhere.md"), path);
        var content = await File.ReadAllTextAsync(path);
        Assert.StartsWith("---", content);
        Assert.Contains("tenant: my-channel", content);
        Assert.Contains("kind: Newsletter", content);
        Assert.Contains("model: claude-cli", content);
        Assert.Contains("# Big News", content);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "newsletter"), "*.tmp"));
    }

    [Fact]
    public async Task Collision_appends_counter()
    {
        var (tenant, draft) = Make();
        var delivery = new FileShareDraftDelivery();

        var p1 = await delivery.DeliverAsync(tenant, new RecipeOutput(), draft);
        var p2 = await delivery.DeliverAsync(tenant, new RecipeOutput(), draft);

        Assert.NotEqual(p1, p2);
        Assert.EndsWith("-2.md", p2);
    }

    [Fact]
    public async Task Unreachable_output_path_throws_so_pipeline_can_record_failure()
    {
        var (tenant, draft) = Make();
        tenant.OutputFolderPath = @"Q:\no-such-drive\x";
        var delivery = new FileShareDraftDelivery();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            delivery.DeliverAsync(tenant, new RecipeOutput(), draft));
    }
}
