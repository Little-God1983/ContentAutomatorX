namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Curated email-safe font stacks (spec §9.2). Keys are stored in
/// TenantBranding.FontKey; unknown keys fall back to the default stack.</summary>
public static class EmailFonts
{
    public const string DefaultKey = "segoe";

    public static readonly IReadOnlyDictionary<string, (string Label, string Stack)> All =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["segoe"] = ("Segoe UI (default)", "Segoe UI,Arial,sans-serif"),
            ["arial"] = ("Arial", "Arial,Helvetica,sans-serif"),
            ["helvetica"] = ("Helvetica", "Helvetica,Arial,sans-serif"),
            ["georgia"] = ("Georgia (serif)", "Georgia,'Times New Roman',serif"),
            ["verdana"] = ("Verdana", "Verdana,Geneva,sans-serif"),
            ["trebuchet"] = ("Trebuchet MS", "'Trebuchet MS',Helvetica,sans-serif"),
        };

    public static string Stack(string? key) =>
        key is not null && All.TryGetValue(key, out var font) ? font.Stack : All[DefaultKey].Stack;
}
