namespace ContentAutomatorX.Domain.Entities;

/// <summary>One row per (tenant, job) holding that tenant's LLM choice for that job. A row with
/// <see cref="Job"/> = null is the tenant's default that every job inherits from; a row with a
/// non-null Job (an <c>LlmJobs</c> key) overrides the default for that one job. Absent rows =
/// tenant never configured = fall back to appsettings, then to the CLI default.
/// Named TenantLlmSetting rather than LlmSetting so it does not read as a typo
/// of the LlmSettings value record.</summary>
public class TenantLlmSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>An <c>LlmJobs</c> key for a per-job override, or null for the tenant default.
    /// Mirrors PromptTemplate.TenantId's "specific row falls back to a general row" shape.</summary>
    public string? Job { get; set; }

    public string Model { get; set; } = "";    // "" = omit --model
    public string Effort { get; set; } = "";   // "" = omit --effort
}
