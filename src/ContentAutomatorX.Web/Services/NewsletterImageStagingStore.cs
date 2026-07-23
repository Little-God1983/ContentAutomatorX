using ContentAutomatorX.Application.Newsletter;
using Microsoft.AspNetCore.Components.Forms;

namespace ContentAutomatorX.Web.Services;

/// <summary>
/// Stages newsletter images on disk under <c>data/newsletter-images</c> and serves them at
/// <c>/newsletter-images/{file}</c> (see the static-file mapping in <c>Program.cs</c>). A section's
/// <see cref="Domain.Entities.IssueSection.ImageKey"/> holds just the bare file name; every upload
/// or URL import gets a fresh random name. Twin of <see cref="TenantAvatarStore"/> — but unlike
/// avatars these images are meant for email, so PR 2 promotes them to R2 when a draft is posted.
/// </summary>
public sealed class NewsletterImageStagingStore
{
    /// <summary>Request path the staging directory is served under.</summary>
    public const string RequestPath = NewsletterImageStaging.RequestPath;

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
    private readonly HttpClient _http;

    public NewsletterImageStagingStore(IWebHostEnvironment env, IHttpClientFactory httpFactory)
        : this(Path.Combine(env.ContentRootPath, "data", "newsletter-images"),
               httpFactory.CreateClient(nameof(NewsletterImageStagingStore))) { }

    // Test-friendly: a bare directory + a client the caller controls.
    public NewsletterImageStagingStore(string dir, HttpClient http)
    {
        _dir = dir;
        _http = http;
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Physical directory the files live in (used to serve them).</summary>
    public string DirectoryPath => _dir;

    /// <summary>App-relative URL for a stored file name, or null when there is none.</summary>
    public static string? UrlFor(string? key) =>
        string.IsNullOrEmpty(key) ? null : $"{RequestPath}/{key}";

    /// <summary>Persists an uploaded file and returns its file name (the value to store in
    /// <c>ImageKey</c>). Throws <see cref="InvalidOperationException"/> (user-facing message) when
    /// the type is unsupported or the file is too large.</summary>
    public async Task<string> SaveAsync(IBrowserFile file, CancellationToken ct = default)
    {
        if (!AllowedTypes.TryGetValue(file.ContentType, out var ext))
            throw new InvalidOperationException("Unsupported image type — use PNG, JPEG, WEBP or GIF.");
        if (file.Size > MaxBytes)
            throw new InvalidOperationException($"Image is too large (max {MaxBytes / (1024 * 1024)} MB).");

        await using var src = file.OpenReadStream(MaxBytes, ct);
        return await SaveStreamAsync(src, ext, ct);
    }

    public async Task<string> SaveStreamAsync(Stream src, string ext, CancellationToken ct)
    {
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var full = Path.Combine(_dir, fileName);
        await using var dest = File.Create(full);
        await src.CopyToAsync(dest, ct);
        return fileName;
    }

    /// <summary>Downloads an image from an absolute http(s) URL, validates it by magic bytes (not the
    /// server's Content-Type header, which can lie), stages it, and returns the file name. Hard-fails
    /// with a user-facing <see cref="InvalidOperationException"/> on a bad URL, a download error, a
    /// non-image body, or oversize — no partial/hotlink fallback is stored (spec D4).</summary>
    public async Task<string> SaveFromUrlAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Enter an absolute http(s) image URL.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        byte[] bytes;
        try
        {
            using var resp = await _http.GetAsync(uri, cts.Token);
            resp.EnsureSuccessStatusCode();
            bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new InvalidOperationException("Couldn't download that image — check the URL.");
        }

        if (bytes.Length > MaxBytes)
            throw new InvalidOperationException($"Image is too large (max {MaxBytes / (1024 * 1024)} MB).");

        var ext = SniffImageExtension(bytes)
            ?? throw new InvalidOperationException("That URL is not a PNG, JPEG, WEBP or GIF image.");

        await using var src = new MemoryStream(bytes);
        return await SaveStreamAsync(src, ext, ct);
    }

    // Trust magic bytes, not the server's Content-Type header — a server can return an HTML error
    // page as image/png, and we must never stage that as an image.
    private static string? SniffImageExtension(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return ".gif";
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
            && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return ".webp";
        return null;
    }

    /// <summary>Deletes the file backing <paramref name="key"/> if present. Best-effort; never throws.</summary>
    public void Delete(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        // Only ever touch a bare file name we generated — never a path that could escape the folder.
        if (Path.GetFileName(key) != key) return;
        try
        {
            var full = Path.Combine(_dir, key);
            if (File.Exists(full)) File.Delete(full);
        }
        catch (IOException) { /* best-effort cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
    }
}
