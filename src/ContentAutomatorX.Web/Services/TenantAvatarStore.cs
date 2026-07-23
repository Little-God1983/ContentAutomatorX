using Microsoft.AspNetCore.Components.Forms;

namespace ContentAutomatorX.Web.Services;

/// <summary>
/// Stores tenant profile images on disk under <c>data/avatars</c> and serves them at
/// <c>/avatars/{file}</c> (see the static-file mapping in <c>Program.cs</c>). A tenant's
/// <see cref="Domain.Entities.Tenant.AvatarPath"/> holds just the bare file name; every
/// upload gets a fresh random name so replacing an image never serves a browser-cached
/// stale one. These images are for the in-app picker/switcher only — unlike newsletter
/// images they are never emailed, so they need no external (R2) hosting.
/// </summary>
public sealed class TenantAvatarStore
{
    /// <summary>Request path the avatars directory is served under.</summary>
    public const string RequestPath = "/avatars";

    private const long MaxBytes = 5 * 1024 * 1024;   // 5 MB

    // Content-type → extension allow-list. Anything else is rejected rather than trusted.
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
    };

    private readonly string _dir;

    public TenantAvatarStore(IWebHostEnvironment env)
    {
        _dir = Path.Combine(env.ContentRootPath, "data", "avatars");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Physical directory the avatar files live in (used to serve them).</summary>
    public string DirectoryPath => _dir;

    /// <summary>App-relative URL for a stored avatar file name, or null when there is none.</summary>
    public static string? UrlFor(string? avatarPath) =>
        string.IsNullOrEmpty(avatarPath) ? null : $"{RequestPath}/{avatarPath}";

    /// <summary>
    /// Persists <paramref name="file"/> as a new avatar and returns its file name (the value to
    /// store in <c>AvatarPath</c>). Throws <see cref="InvalidOperationException"/> with a
    /// user-facing message when the type is unsupported or the file is too large.
    /// </summary>
    public async Task<string> SaveAsync(IBrowserFile file, CancellationToken ct = default)
    {
        if (!AllowedTypes.TryGetValue(file.ContentType, out var ext))
            throw new InvalidOperationException("Unsupported image type — use PNG, JPEG, WEBP or GIF.");
        if (file.Size > MaxBytes)
            throw new InvalidOperationException($"Image is too large (max {MaxBytes / (1024 * 1024)} MB).");

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var full = Path.Combine(_dir, fileName);
        await using var dest = File.Create(full);
        await using var src = file.OpenReadStream(MaxBytes, ct);
        await src.CopyToAsync(dest, ct);
        return fileName;
    }

    /// <summary>Deletes the file backing <paramref name="avatarPath"/> if present. Best-effort; never throws.</summary>
    public void Delete(string? avatarPath)
    {
        if (string.IsNullOrEmpty(avatarPath)) return;
        // Only ever touch a bare file name we generated — never a path that could escape the folder.
        if (Path.GetFileName(avatarPath) != avatarPath) return;
        try
        {
            var full = Path.Combine(_dir, avatarPath);
            if (File.Exists(full)) File.Delete(full);
        }
        catch (IOException) { /* best-effort cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
    }
}
