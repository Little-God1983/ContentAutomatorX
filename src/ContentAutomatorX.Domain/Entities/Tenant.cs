namespace ContentAutomatorX.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string VoiceProfile { get; set; } = "";
    public string OutputFolderPath { get; set; } = "";
    public string DefaultHeaderMd { get; set; } = "";
    public string DefaultFooterMd { get; set; } = "";
    public string BrandingJson { get; set; } = "{}";   // TenantBranding
    public string SenderIdentity { get; set; } = "";   // "Name, street, city, country" for the compliance footer
    public string? AvatarPath { get; set; }            // file name under data/avatars for the picker; null = initials
    public bool IsActive { get; set; } = true;
}
