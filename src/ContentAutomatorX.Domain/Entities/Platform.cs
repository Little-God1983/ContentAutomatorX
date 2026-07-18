namespace ContentAutomatorX.Domain.Entities;

public class Platform
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Type { get; set; }            // PlatformTypes.*
    public required string DisplayName { get; set; }
    public string ColorHex { get; set; } = "#1e88e5";
    public string ConfigJson { get; set; } = "{}";        // MailerLite: {groupId, groupName, fromName, fromEmail}
    public string? CredentialRef { get; set; }            // ICredentialStore blob name, e.g. "mailerlite:<id>"
    public bool IsEnabled { get; set; } = true;
}
