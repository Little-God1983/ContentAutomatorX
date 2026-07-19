using System.Text.Json;

namespace ContentAutomatorX.Domain.Models;

public record TenantBranding(string? AccentColorHex, string? LogoUrl, string? FontKey)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly TenantBranding Empty = new(null, null, null);

    public static TenantBranding Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try { return JsonSerializer.Deserialize<TenantBranding>(json, JsonOpts) ?? Empty; }
        catch (JsonException) { return Empty; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);
}
