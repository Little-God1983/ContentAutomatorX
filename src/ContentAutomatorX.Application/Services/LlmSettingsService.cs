using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

/// <summary>Resolves a tenant's LLM choice: its own row, else appsettings, else
/// "omit both flags". There is no cache — one indexed SQLite row is microseconds
/// against a multi-second CLI invocation. The reads are AsNoTracking, which is
/// what actually makes "fresh every call" true: the scoped AppDbContext lives for
/// a whole Blazor circuit, so a tracking query would let EF identity resolution
/// return the snapshot this circuit loaded earlier and a save made in another tab
/// (another circuit) would never be seen here.</summary>
/// <param name="fallback">Values from appsettings, wrapped in their own type so
/// nothing can inject them where a tenant's resolved settings are meant. Carries a
/// plain LlmSettings because Application must not reference Infrastructure.</param>
public class LlmSettingsService(IAppDbContext db, LlmFallbackSettings fallback) : ILlmSettingsProvider
{
    public async Task<LlmSettings> GetAsync(Guid tenantId, string? job = null, CancellationToken ct = default)
    {
        // The fallback is sanitised too: a typo'd Claude:Model must not reach the CLI
        // just because it arrived through appsettings rather than through the UI.
        var fallbackModel = SafeModel(fallback.Value.Model);

        // Load every row this tenant has (at most one default plus one per job — a handful) and
        // pick in memory. Avoids EF's null-parameter equality subtleties and stays a single
        // TenantId-indexed read; the reads are AsNoTracking so a save from another circuit is seen.
        var rows = await db.TenantLlmSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId).ToListAsync(ct);
        var jobRow = job is null ? null : rows.FirstOrDefault(s => s.Job == job);
        var defaultRow = rows.FirstOrDefault(s => s.Job == null);

        // Each field falls back independently across every step — job override, then tenant
        // default, then appsettings — so a job that pins only a model still inherits the tenant's
        // effort (and the reverse). SafeModel runs on every stored source, including the job row,
        // because a job-row model reaches --model by the same process-argument path.
        var model = SafeModel(jobRow?.Model);
        if (model.Length == 0) model = SafeModel(defaultRow?.Model);
        if (model.Length == 0) model = fallbackModel;

        var effort = LlmSettings.ParseEffort(jobRow?.Effort);
        if (effort == LlmEffort.Default) effort = LlmSettings.ParseEffort(defaultRow?.Effort);
        if (effort == LlmEffort.Default) effort = fallback.Value.Effort;

        return new LlmSettings(model, effort);
    }

    public async Task<LlmSettings> GetStoredAsync(Guid tenantId, string? job = null, CancellationToken ct = default)
    {
        var row = await FindRowAsync(db.TenantLlmSettings.AsNoTracking(), tenantId, job, ct);
        return row is null
            ? LlmSettings.Inherit
            : new LlmSettings(SafeModel(row.Model), LlmSettings.ParseEffort(row.Effort));
    }

    /// <summary>Locates the single (tenant, job) row. <paramref name="job"/> null resolves the
    /// tenant-default row (Job IS NULL); a non-null job resolves that override row.</summary>
    private static Task<TenantLlmSetting?> FindRowAsync(
        IQueryable<TenantLlmSetting> query, Guid tenantId, string? job, CancellationToken ct) =>
        job is null
            ? query.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Job == null, ct)
            : query.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Job == job, ct);

    /// <summary>Blank means "unset". Anything else becomes a process argument, so it
    /// must pass the same rule SaveAsync enforces. A value that fails it can only have
    /// arrived by bypassing SaveAsync — a hand-edited row, a seed migration, a future
    /// import — so degrade it to "unset" rather than throwing, exactly as
    /// LlmSettings.ParseEffort degrades a garbage effort string to Default. Bricking a
    /// tenant's generation over a bad stored value would be worse than ignoring it.</summary>
    private static string SafeModel(string? model)
    {
        var trimmed = model?.Trim() ?? "";
        return trimmed.Length == 0 || LlmModelName.IsValid(trimmed) ? trimmed : "";
    }

    public async Task SaveAsync(Guid tenantId, LlmSettings settings, string? job = null, CancellationToken ct = default)
    {
        var model = settings.Model?.Trim() ?? "";
        // Blank means "unset"; anything else must survive becoming a process argument.
        // Enforced here and not only in the UI, so it holds for any caller.
        if (model.Length > 0 && !LlmModelName.IsValid(model))
            throw new ArgumentException(
                $"'{model}' is not a valid model name. Use letters, digits, dot, underscore, " +
                $"hyphen or square brackets, up to {LlmModelName.MaxLength} characters.",
                nameof(settings));

        var row = await FindRowAsync(db.TenantLlmSettings, tenantId, job, ct);

        // A per-job override that pins neither field is not an override — delete it (or never create
        // it) so "no override = no row" stays true and the job table does not accumulate empty rows.
        // The tenant-default row (job == null) is never auto-deleted: saving a blank default is a
        // legitimate "unset both flags" that the resolver and its existing tests depend on.
        if (job is not null && model.Length == 0 && settings.Effort == LlmEffort.Default)
        {
            if (row is not null) db.TenantLlmSettings.Remove(row);
            await db.SaveChangesAsync(ct);
            return;
        }

        if (row is null)
        {
            row = new TenantLlmSetting { TenantId = tenantId, Job = job };
            db.TenantLlmSettings.Add(row);
        }
        row.Model = model;
        row.Effort = LlmSettings.ToStorage(settings.Effort);
        await db.SaveChangesAsync(ct);
    }
}
