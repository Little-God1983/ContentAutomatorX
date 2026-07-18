using ContentAutomatorX.Web.Services;

namespace ContentAutomatorX.IntegrationTests;

public class TenantSlugTests
{
    [Theory]
    [InlineData("Alpha Tenant", "alpha-tenant")]
    [InlineData("  My_Cool Channel  ", "my-cool-channel")]
    [InlineData("Über Cool!!", "ber-cool")]      // non-ASCII and punctuation dropped
    [InlineData("A -- B", "a-b")]                // separator runs collapse to one hyphen
    [InlineData("-lead and trail-", "lead-and-trail")]
    [InlineData("!!!", "")]                      // nothing usable → empty
    [InlineData("", "")]
    public void Derive_produces_expected_slug(string name, string expected) =>
        Assert.Equal(expected, TenantSlug.Derive(name));
}
