using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class RecipeService(IAppDbContext db)
{
    public Task<List<Recipe>> ListAsync(Guid tenantId) =>
        db.Recipes.Where(r => r.TenantId == tenantId).OrderBy(r => r.Name).ToListAsync();

    public Task<Recipe?> GetAsync(Guid id) => db.Recipes.FirstOrDefaultAsync(r => r.Id == id);

    public Task<PromptTemplate?> GetTemplateAsync(Guid id) =>
        db.PromptTemplates.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Recipe> CreateAsync(Recipe recipe)
    {
        if (recipe.PromptTemplateId == Guid.Empty)
        {
            var systemDefault = await db.PromptTemplates
                .FirstOrDefaultAsync(p => p.TenantId == null && p.Kind == recipe.Kind);
            var clone = new PromptTemplate
            {
                TenantId = recipe.TenantId,
                Kind = recipe.Kind,
                Template = systemDefault?.Template ?? DefaultTemplates.GetFor(recipe.Kind)
            };
            db.PromptTemplates.Add(clone);
            recipe.PromptTemplateId = clone.Id;
        }
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return recipe;
    }

    public Task UpdateAsync() => db.SaveChangesAsync();

    public async Task DeleteAsync(Guid id)
    {
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id);
        if (recipe is null) return;
        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();
    }
}
