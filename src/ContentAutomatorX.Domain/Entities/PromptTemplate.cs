namespace ContentAutomatorX.Domain.Entities;

public class PromptTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }                // null = system default
    public required string Kind { get; set; }
    public required string Template { get; set; }      // {voice_profile} {tone_modifiers} {items} {extra_instructions}
}
