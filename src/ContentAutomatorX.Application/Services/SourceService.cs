using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class SourceService(IAppDbContext db)
{
    public Task<List<Source>> ListAsync(Guid tenantId) =>
        db.Sources.Where(s => s.TenantId == tenantId).OrderBy(s => s.DisplayName).ToListAsync();

    public async Task<Source> CreateAsync(Source source)
    {
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    public Task UpdateAsync() => db.SaveChangesAsync();

    /// <summary>
    /// Deletes the source and its ingested inbox items. Throws when the source id is
    /// still referenced by an automation or a not-yet-published issue's source set.
    /// Returns the number of inbox items removed alongside the source.
    /// </summary>
    public async Task<int> DeleteAsync(Guid id)
    {
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == id);
        if (source is null) return 0;

        var usedBy = new List<string>();

        var recipes = await db.Recipes.Where(r => r.TenantId == source.TenantId).ToListAsync();
        usedBy.AddRange(recipes
            .Where(r => ReferencesSource(r.SourceIdsJson, source.Id))
            .Select(r => $"automation '{r.Name}'"));

        // published issues are history — only open ones still need their source set intact
        var posts = await db.Posts
            .Where(p => p.TenantId == source.TenantId && p.Status != PostStatus.Published && p.SourceIdsJson != null)
            .ToListAsync();
        usedBy.AddRange(posts
            .Where(p => ReferencesSource(p.SourceIdsJson, source.Id))
            .Select(p => $"issue '{p.Title}'"));

        if (usedBy.Count > 0)
            throw new InvalidOperationException(
                $"'{source.DisplayName}' is in use by {string.Join(", ", usedBy)} — remove it there first.");

        var items = await db.ContentItems.Where(i => i.SourceId == id).ToListAsync();
        db.ContentItems.RemoveRange(items);
        db.Sources.Remove(source);
        await db.SaveChangesAsync();
        return items.Count;
    }

    private static bool ReferencesSource(string? sourceIdsJson, Guid sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceIdsJson)) return false;
        try
        {
            return (JsonSerializer.Deserialize<Guid[]>(sourceIdsJson) ?? []).Contains(sourceId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
