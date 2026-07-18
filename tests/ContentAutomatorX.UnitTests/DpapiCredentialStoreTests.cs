using ContentAutomatorX.Infrastructure.Security;

namespace ContentAutomatorX.UnitTests;

public class DpapiCredentialStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cax-secrets-{Guid.NewGuid():N}");

    [Fact]
    public async Task Round_trips_a_secret_and_stores_it_encrypted()
    {
        var store = new DpapiCredentialStore(_dir);
        await store.SetAsync("mailerlite:abc", "s3cret-key");

        Assert.Equal("s3cret-key", await store.GetAsync("mailerlite:abc"));
        var file = Directory.GetFiles(_dir).Single();
        Assert.DoesNotContain("s3cret-key", await File.ReadAllTextAsync(file)); // not plaintext
    }

    [Fact]
    public async Task Get_missing_returns_null_and_delete_is_idempotent()
    {
        var store = new DpapiCredentialStore(_dir);
        Assert.Null(await store.GetAsync("nope"));
        await store.DeleteAsync("nope"); // must not throw
        await store.SetAsync("a", "1");
        await store.DeleteAsync("a");
        Assert.Null(await store.GetAsync("a"));
    }

    [Fact]
    public async Task Name_with_separator_chars_is_sanitized_to_a_safe_filename()
    {
        var store = new DpapiCredentialStore(_dir);
        await store.SetAsync(@"weird:name/with\chars", "v");
        Assert.Equal("v", await store.GetAsync(@"weird:name/with\chars"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
