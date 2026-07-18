namespace ContentAutomatorX.Domain.Abstractions;

/// <summary>Secrets live outside SQLite (Phase 1 decision). Names are logical, e.g. "mailerlite:{platformId}".</summary>
public interface ICredentialStore
{
    Task SetAsync(string name, string secret, CancellationToken ct = default);
    Task<string?> GetAsync(string name, CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
}
