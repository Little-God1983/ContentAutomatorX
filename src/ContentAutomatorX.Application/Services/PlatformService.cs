using System.Text.Json;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class PlatformService(IAppDbContext db, ICredentialStore credentials, IMailerLiteClient mailerLite)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<Platform> GetOrCreateMailerLiteAsync(Guid tenantId, CancellationToken ct = default)
    {
        var existing = await db.Platforms
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Type == PlatformTypes.MailerLite, ct);
        if (existing is not null) return existing;

        var platform = new Platform { TenantId = tenantId, Type = PlatformTypes.MailerLite, DisplayName = "MailerLite" };
        platform.CredentialRef = $"mailerlite:{platform.Id}";
        db.Platforms.Add(platform);
        await db.SaveChangesAsync(ct);
        return platform;
    }

    public MailerLiteConfig GetConfig(Platform platform) =>
        JsonSerializer.Deserialize<MailerLiteConfig>(platform.ConfigJson, JsonOpts)
            ?? new MailerLiteConfig(null, null, null, null);

    public async Task SaveConfigAsync(Platform platform, MailerLiteConfig config, string? colorHex = null,
        CancellationToken ct = default)
    {
        platform.ConfigJson = JsonSerializer.Serialize(config, JsonOpts);
        if (!string.IsNullOrWhiteSpace(colorHex)) platform.ColorHex = colorHex;
        await db.SaveChangesAsync(ct);
    }

    public Task SetApiKeyAsync(Platform platform, string apiKey, CancellationToken ct = default) =>
        credentials.SetAsync(platform.CredentialRef ?? $"mailerlite:{platform.Id}", apiKey, ct);

    public Task<string?> GetApiKeyAsync(Platform platform, CancellationToken ct = default) =>
        credentials.GetAsync(platform.CredentialRef ?? $"mailerlite:{platform.Id}", ct);

    public async Task<bool> TestAsync(Platform platform, CancellationToken ct = default)
    {
        var key = await GetApiKeyAsync(platform, ct);
        return key is not null && await mailerLite.TestAsync(key, ct);
    }

    public async Task<IReadOnlyList<MailerLiteGroup>> ListGroupsAsync(Platform platform, CancellationToken ct = default)
    {
        var key = await GetApiKeyAsync(platform, ct)
            ?? throw new InvalidOperationException("No API key stored — set it on the Platforms page.");
        return await mailerLite.ListGroupsAsync(key, ct);
    }
}
