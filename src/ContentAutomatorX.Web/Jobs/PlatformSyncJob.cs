using ContentAutomatorX.Application.Services;

namespace ContentAutomatorX.Web.Jobs;

public class PlatformSyncJob(IServiceScopeFactory scopeFactory, ILogger<PlatformSyncJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<PostSyncService>();
                var touched = await sync.TickAsync(DateTimeOffset.UtcNow, ct);
                if (touched > 0) logger.LogInformation("platform sync touched {Count} posts", touched);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "platform sync tick failed"); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
