namespace ContentAutomatorX.Domain.Entities;

/// <summary>One row per tenant holding that tenant's LLM choice. Absent row =
/// tenant never configured = fall back to appsettings, then to the CLI default.
/// Named TenantLlmSetting rather than LlmSetting so it does not read as a typo
/// of the LlmSettings value record.</summary>
public class TenantLlmSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Model { get; set; } = "";    // "" = omit --model
    public string Effort { get; set; } = "";   // "" = omit --effort
}
