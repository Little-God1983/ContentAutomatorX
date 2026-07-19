using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ContentAutomatorX.Domain.Abstractions;

namespace ContentAutomatorX.Infrastructure.Security;

/// <summary>DPAPI (CurrentUser) blobs, one file per secret. Windows-only by design for the
/// local phase; a server deployment later swaps this implementation behind ICredentialStore.</summary>
[SupportedOSPlatform("windows")]
public class DpapiCredentialStore(string? rootDir = null) : ICredentialStore
{
    private readonly string _root = rootDir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ContentAutomatorX", "secrets");

    public async Task SetAsync(string name, string secret, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(PathFor(name), blob, ct);
    }

    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
    {
        byte[] blob;
        try
        {
            blob = await File.ReadAllBytesAsync(PathFor(name), ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null; // absent — no Exists pre-check, so a file removed mid-flight is just "not found"
        }

        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            // Corrupted blob or written by a different Windows user: treat as absent so the UI's
            // "no key stored" path prompts for re-entry instead of crashing the page.
            return null;
        }
    }

    public Task DeleteAsync(string name, CancellationToken ct = default)
    {
        try
        {
            File.Delete(PathFor(name)); // no-op when the file is already gone
        }
        catch (DirectoryNotFoundException)
        {
        }
        return Task.CompletedTask;
    }

    private string PathFor(string name)
    {
        var safe = new StringBuilder(name.Length);
        foreach (var c in name)
            safe.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..8];
        return Path.Combine(_root, $"{safe}-{hash}.bin");
    }
}
