using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class PostSyncService(IAppDbContext db, PlatformService platforms, IMailerLiteClient mailerLite)
{
    private record StatsSnapshot(DateTimeOffset RefreshedAt, int? Sent, int? Opens, int? Clicks);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<int> TickAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var thirtyDaysAgo = now.AddDays(-30);
        var allCandidates = await db.Posts
            .Where(p => p.ExternalId != null)
            .ToListAsync(ct);
        var candidates = allCandidates
            .Where(p => p.Status == PostStatus.Pushed ||
                       (p.Status == PostStatus.Published && p.PublishedAt > thirtyDaysAgo))
            .ToList();

        var touched = 0;
        foreach (var post in candidates)
        {
            try
            {
                if (post.Status == PostStatus.Published && !StatsStale(post, now)) continue;

                var platform = await db.Platforms.SingleAsync(p => p.Id == post.PlatformId, ct);
                var key = await platforms.GetApiKeyAsync(platform, ct);
                if (key is null) continue;

                var status = await mailerLite.GetStatusAsync(key, post.ExternalId!, ct);
                if (post.Status == PostStatus.Pushed && status.Status != "sent") continue;

                if (post.Status == PostStatus.Pushed)
                {
                    post.Status = PostStatus.Published;
                    post.PublishedAt = now;
                }
                post.StatsJson = JsonSerializer.Serialize(
                    new StatsSnapshot(now, status.Sent, status.OpensCount, status.ClicksCount), JsonOpts);
                await db.SaveChangesAsync(ct);
                touched++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                // one failing post never blocks the rest; next tick retries (idempotent)
            }
        }
        return touched;
    }

    private static bool StatsStale(Post post, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(post.StatsJson);
            return !doc.RootElement.TryGetProperty("refreshedAt", out var r)
                || r.GetDateTimeOffset() < now.AddHours(-24);
        }
        catch (JsonException) { return true; }
    }
}
