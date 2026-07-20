using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

/// <summary>Resolves a tenant's LLM choice: its own row, else appsettings, else
/// "omit both flags". Read fresh on every call — one indexed SQLite row is
/// microseconds against a multi-second CLI invocation, so there is no cache and
/// therefore no cache-invalidation bug when a save lands mid-session.</summary>
/// <param name="fallback">Values from appsettings. A plain LlmSettings rather
/// than ClaudeCliOptions because Application must not reference Infrastructure.</param>
public class LlmSettingsService(IAppDbContext db, LlmSettings fallback) : ILlmSettingsProvider
{
    public async Task<LlmSettings> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await db.TenantLlmSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (row is null) return fallback;

        // Each field falls back independently: a tenant may pin a model while
        // leaving effort alone, or the reverse.
        var model = string.IsNullOrWhiteSpace(row.Model) ? fallback.Model : row.Model.Trim();
        var effort = LlmSettings.ParseEffort(row.Effort);
        if (effort == LlmEffort.Default) effort = fallback.Effort;
        return new LlmSettings(model, effort);
    }

    public async Task SaveAsync(Guid tenantId, LlmSettings settings, CancellationToken ct = default)
    {
        var model = settings.Model?.Trim() ?? "";
        // Blank means "unset"; anything else must survive becoming a process argument.
        // Enforced here and not only in the UI, so it holds for any caller.
        if (model.Length > 0 && !LlmModelName.IsValid(model))
            throw new ArgumentException(
                $"'{model}' is not a valid model name. Use letters, digits, dot, underscore, " +
                $"hyphen or square brackets, up to {LlmModelName.MaxLength} characters.",
                nameof(settings));

        var row = await db.TenantLlmSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (row is null)
        {
            row = new TenantLlmSetting { TenantId = tenantId };
            db.TenantLlmSettings.Add(row);
        }
        row.Model = model;
        row.Effort = LlmSettings.ToStorage(settings.Effort);
        await db.SaveChangesAsync(ct);
    }
}
