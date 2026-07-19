using System.Text.Json;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.IntegrationTests;

public class PostSyncServiceTests
{
    private static async Task<(TestDb Test, PostSyncService Sync, FakeMailerLite Ml, Post Post)> BuildAsync(
        PostStatus status, string? statsJson = null, DateTimeOffset? publishedAt = null)
    {
        var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-sync" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML", CredentialRef = "k" };
        var post = new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "t", Status = status, ExternalId = "c-1",
            StatsJson = statsJson ?? "{}", PublishedAt = publishedAt
        };
        test.Db.AddRange(tenant, platform, post);
        await test.Db.SaveChangesAsync();

        var creds = new InMemoryCredentials();
        await creds.SetAsync("k", "KEY");
        var ml = new FakeMailerLite();
        var sync = new PostSyncService(test.Db, new PlatformService(test.Db, creds, ml), ml);
        return (test, sync, ml, post);
    }

    [Fact]
    public async Task Pushed_post_becomes_published_when_campaign_is_sent()
    {
        var (test, sync, ml, post) = await BuildAsync(PostStatus.Pushed);
        using var _ = test;
        ml.NextStatus = new("sent", 1204, 577, 89);
        var now = DateTimeOffset.UtcNow;

        var touched = await sync.TickAsync(now);

        Assert.Equal(1, touched);
        var reloaded = await test.Db.Posts.FindAsync(post.Id);
        Assert.Equal(PostStatus.Published, reloaded!.Status);
        Assert.Equal(now, reloaded.PublishedAt);
        using var stats = JsonDocument.Parse(reloaded.StatsJson);
        Assert.Equal(1204, stats.RootElement.GetProperty("sent").GetInt32());
    }

    [Fact]
    public async Task Pushed_post_still_draft_in_mailerlite_is_untouched()
    {
        var (test, sync, ml, post) = await BuildAsync(PostStatus.Pushed);
        using var _ = test;
        ml.NextStatus = new("draft", null, null, null);

        Assert.Equal(0, await sync.TickAsync(DateTimeOffset.UtcNow));
        Assert.Equal(PostStatus.Pushed, (await test.Db.Posts.FindAsync(post.Id))!.Status);
    }

    [Fact]
    public async Task Recent_published_post_with_stale_stats_gets_refreshed()
    {
        var staleStats = $$"""{"refreshedAt":"{{DateTimeOffset.UtcNow.AddHours(-25):O}}","sent":1204,"opens":100,"clicks":5}""";
        var (test, sync, ml, post) = await BuildAsync(PostStatus.Published, staleStats,
            publishedAt: DateTimeOffset.UtcNow.AddDays(-3));
        using var _ = test;
        ml.NextStatus = new("sent", 1204, 601, 92);

        Assert.Equal(1, await sync.TickAsync(DateTimeOffset.UtcNow));
        using var stats = JsonDocument.Parse((await test.Db.Posts.FindAsync(post.Id))!.StatsJson);
        Assert.Equal(601, stats.RootElement.GetProperty("opens").GetInt32());
    }

    [Fact]
    public async Task Old_published_post_is_left_alone()
    {
        var (test, sync, ml, _) = await BuildAsync(PostStatus.Published,
            $$"""{"refreshedAt":"{{DateTimeOffset.UtcNow.AddDays(-2):O}}","sent":1,"opens":1,"clicks":0}""",
            publishedAt: DateTimeOffset.UtcNow.AddDays(-45));
        using var _1 = test;
        Assert.Equal(0, await sync.TickAsync(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Non_date_refreshedAt_counts_as_stale_and_gets_refreshed()
    {
        var (test, sync, ml, post) = await BuildAsync(PostStatus.Published,
            """{"refreshedAt":"not-a-date"}""", publishedAt: DateTimeOffset.UtcNow.AddDays(-3));
        using var _ = test;
        ml.NextStatus = new("sent", 1204, 601, 92);
        var now = DateTimeOffset.UtcNow;

        var touched = await sync.TickAsync(now);

        Assert.Equal(1, touched);
        Assert.Contains("c-1", ml.StatusCalls);
        using var stats = JsonDocument.Parse((await test.Db.Posts.FindAsync(post.Id))!.StatsJson);
        Assert.Equal(601, stats.RootElement.GetProperty("opens").GetInt32());
    }

    [Fact]
    public async Task Malformed_stats_json_counts_as_stale_and_gets_refreshed()
    {
        var (test, sync, ml, post) = await BuildAsync(PostStatus.Published,
            "{{{", publishedAt: DateTimeOffset.UtcNow.AddDays(-3));
        using var _ = test;
        ml.NextStatus = new("sent", 1204, 601, 92);
        var now = DateTimeOffset.UtcNow;

        var touched = await sync.TickAsync(now);

        Assert.Equal(1, touched);
        Assert.Contains("c-1", ml.StatusCalls);
        using var stats = JsonDocument.Parse((await test.Db.Posts.FindAsync(post.Id))!.StatsJson);
        Assert.Equal(601, stats.RootElement.GetProperty("opens").GetInt32());
    }

    [Fact]
    public async Task Missing_api_key_skips_the_post_without_touching_it()
    {
        var test = TestDb.Create();
        using var _ = test;
        var tenant = new Tenant { Name = "T", Slug = "t-sync-nokey" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML", CredentialRef = "k" };
        var post = new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "t", Status = PostStatus.Pushed, ExternalId = "c-1", StatsJson = "{}"
        };
        test.Db.AddRange(tenant, platform, post);
        await test.Db.SaveChangesAsync();

        var creds = new InMemoryCredentials(); // note: no SetAsync("k", ...) — key intentionally missing
        var ml = new FakeMailerLite();
        var sync = new PostSyncService(test.Db, new PlatformService(test.Db, creds, ml), ml);

        var touched = await sync.TickAsync(DateTimeOffset.UtcNow);

        Assert.Equal(0, touched);
        Assert.Empty(ml.StatusCalls);
        var reloaded = await test.Db.Posts.FindAsync(post.Id);
        Assert.Equal(PostStatus.Pushed, reloaded!.Status);
        Assert.Equal("{}", reloaded.StatsJson);
    }

    [Fact]
    public async Task One_failing_post_does_not_block_the_next()
    {
        var test = TestDb.Create();
        using var _ = test;
        var tenant = new Tenant { Name = "T", Slug = "t-sync-2" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML", CredentialRef = "k" };
        var post1 = new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "t1", Status = PostStatus.Pushed, ExternalId = "c-1", StatsJson = "{}"
        };
        var post2 = new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "t2", Status = PostStatus.Pushed, ExternalId = "c-2", StatsJson = "{}"
        };
        test.Db.AddRange(tenant, platform, post1, post2);
        await test.Db.SaveChangesAsync();

        var creds = new InMemoryCredentials();
        await creds.SetAsync("k", "KEY");
        var ml = new FakeMailerLite { ThrowForCampaignId = "c-1", NextStatus = new("sent", 10, 5, 1) };
        var sync = new PostSyncService(test.Db, new PlatformService(test.Db, creds, ml), ml);

        var touched = await sync.TickAsync(DateTimeOffset.UtcNow);

        Assert.Equal(1, touched);
        Assert.Contains("c-2", ml.StatusCalls);
        var reloaded1 = await test.Db.Posts.FindAsync(post1.Id);
        Assert.Equal(PostStatus.Pushed, reloaded1!.Status); // first post's failure never advances it
        var reloaded2 = await test.Db.Posts.FindAsync(post2.Id);
        Assert.Equal(PostStatus.Published, reloaded2!.Status); // second post still processed
    }

    [Fact]
    public async Task Cancellation_propagates_out_of_the_tick()
    {
        var (test, sync, ml, _) = await BuildAsync(PostStatus.Pushed);
        using var _1 = test;
        ml.NextStatus = new("sent", 1, 1, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sync.TickAsync(DateTimeOffset.UtcNow, cts.Token));
    }
}
