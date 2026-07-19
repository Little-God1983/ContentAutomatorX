using System.Runtime.Versioning;
using ContentAutomatorX.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace ContentAutomatorX.UnitTests;

[SupportedOSPlatform("windows")]
public class DpapiCredentialStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cax-secrets-{Guid.NewGuid():N}");

    [WindowsOnlyFact]
    public async Task Round_trips_a_secret_and_stores_it_encrypted()
    {
        var store = new DpapiCredentialStore(_dir);
        await store.SetAsync("mailerlite:abc", "s3cret-key");

        Assert.Equal("s3cret-key", await store.GetAsync("mailerlite:abc"));
        var file = Directory.GetFiles(_dir).Single();
        Assert.DoesNotContain("s3cret-key", await File.ReadAllTextAsync(file)); // not plaintext
    }

    [WindowsOnlyFact]
    public async Task Get_missing_returns_null_and_delete_is_idempotent()
    {
        var store = new DpapiCredentialStore(_dir);
        Assert.Null(await store.GetAsync("nope"));
        await store.DeleteAsync("nope"); // must not throw
        await store.SetAsync("a", "1");
        await store.DeleteAsync("a");
        Assert.Null(await store.GetAsync("a"));
    }

    [WindowsOnlyFact]
    public async Task Name_with_separator_chars_is_sanitized_to_a_safe_filename()
    {
        var store = new DpapiCredentialStore(_dir);
        await store.SetAsync(@"weird:name/with\chars", "v");
        Assert.Equal("v", await store.GetAsync(@"weird:name/with\chars"));
    }

    [WindowsOnlyFact]
    public async Task Sanitized_colliding_names_store_distinct_secrets()
    {
        var store = new DpapiCredentialStore(_dir);
        // "a:b" and "a_b" both sanitize to "a_b", but should be stored separately
        await store.SetAsync("a:b", "secret1");
        await store.SetAsync("a_b", "secret2");

        Assert.Equal("secret1", await store.GetAsync("a:b"));
        Assert.Equal("secret2", await store.GetAsync("a_b"));
    }

    [WindowsOnlyFact]
    public async Task Empty_secret_round_trips()
    {
        var store = new DpapiCredentialStore(_dir);
        await store.SetAsync("empty", "");

        Assert.Equal("", await store.GetAsync("empty"));
    }

    [WindowsOnlyFact]
    public async Task Corrupted_blob_reads_as_absent_instead_of_throwing()
    {
        var store = new DpapiCredentialStore(_dir);
        await store.SetAsync("mailerlite:abc", "s3cret");
        var file = Directory.GetFiles(_dir).Single();
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4, 5]); // garbage — Unprotect will fail

        Assert.Null(await store.GetAsync("mailerlite:abc"));
    }

    [WindowsOnlyFact]
    public async Task Corrupted_blob_logs_a_warning_naming_the_credential()
    {
        var logger = new FakeLogger();
        var store = new DpapiCredentialStore(_dir, logger);
        await store.SetAsync("mailerlite:abc", "s3cret");
        var file = Directory.GetFiles(_dir).Single();
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4, 5]); // garbage — Unprotect will fail

        Assert.Null(await store.GetAsync("mailerlite:abc"));

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("mailerlite:abc", warning.Message);
    }

    private sealed class FakeLogger : ILogger<DpapiCredentialStore>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [WindowsOnlyFact]
    public async Task Get_treats_file_vanishing_underneath_it_as_absent()
    {
        var store = new DpapiCredentialStore(_dir);
        await store.SetAsync("gone", "v");
        File.Delete(Directory.GetFiles(_dir).Single()); // simulates the TOCTOU loser side

        Assert.Null(await store.GetAsync("gone"));
    }

    [WindowsOnlyFact]
    public async Task Delete_without_store_directory_does_not_throw()
    {
        var store = new DpapiCredentialStore(Path.Combine(_dir, "never-created"));

        await store.DeleteAsync("nope");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
