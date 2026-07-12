namespace ContentAutomatorX.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string VoiceProfile { get; set; } = "";
    public string OutputFolderPath { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
