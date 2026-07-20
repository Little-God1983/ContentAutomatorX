using ContentAutomatorX.Application.Services;

namespace ContentAutomatorX.Web.Jobs;

/// <summary>Daily sweep for expired issue chat, proposals and revisions. Separate from
/// PlatformSyncJob rather than folded into its hourly tick: retention is not platform sync, and
/// shared ticks are how jobs become junk drawers.</summary>
public class ChatRetentionJob(IServiceScopeFactory scopeFactory, ILogger<ChatRetentionJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var chat = scope.ServiceProvider.GetRequiredService<IssueChatService>();
                var collected = await chat.PurgeAsync(DateTimeOffset.UtcNow, ct);
                if (collected > 0) logger.LogInformation("chat retention collected {Count} issue threads", collected);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "chat retention tick failed"); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
