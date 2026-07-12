using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class DraftService(IAppDbContext db, IDraftDelivery delivery)
{
    public Task<List<Draft>> ListAsync(Guid tenantId, string? kind = null, DraftStatus? status = null)
    {
        var query = db.Drafts.Where(d => d.TenantId == tenantId);
        if (kind is not null) query = query.Where(d => d.Kind == kind);
        if (status is not null) query = query.Where(d => d.Status == status);
        return query.OrderByDescending(d => d.CreatedAt).ToListAsync();
    }

    public Task<Draft?> GetAsync(Guid id) => db.Drafts.FirstOrDefaultAsync(d => d.Id == id);

    public async Task<Draft> RetryDeliveryAsync(Guid draftId)
    {
        var draft = await db.Drafts.FirstAsync(d => d.Id == draftId);
        var tenant = await db.Tenants.FirstAsync(t => t.Id == draft.TenantId);
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == draft.RecipeId);
        var output = recipe is null ? new RecipeOutput()
            : JsonSerializer.Deserialize<RecipeOutput>(recipe.OutputJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RecipeOutput();

        draft.FilePath = await delivery.DeliverAsync(tenant, output, draft);
        draft.Status = DraftStatus.Delivered;
        await db.SaveChangesAsync();
        return draft;
    }
}
