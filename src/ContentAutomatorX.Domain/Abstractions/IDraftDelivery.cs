using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface IDraftDelivery
{
    /// <summary>Writes the draft file and returns the absolute file path.</summary>
    Task<string> DeliverAsync(Tenant tenant, RecipeOutput output, Draft draft, CancellationToken ct = default);
}
