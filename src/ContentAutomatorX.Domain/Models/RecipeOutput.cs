namespace ContentAutomatorX.Domain.Models;

public class RecipeOutput
{
    public string? Subfolder { get; set; }
    public string? FilenamePattern { get; set; }   // tokens: {date} {kind} {slug}; default "{date}-{kind}-{slug}.md"
    public string? TargetPlatform { get; set; }
}
