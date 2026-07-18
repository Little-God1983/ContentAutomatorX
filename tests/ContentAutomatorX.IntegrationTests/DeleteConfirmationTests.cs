using ContentAutomatorX.Web.Services;

namespace ContentAutomatorX.IntegrationTests;

public class DeleteConfirmationTests
{
    [Theory]
    [InlineData("Alpha Media", "Alpha Media", true)]
    [InlineData("  Alpha Media  ", "Alpha Media", true)]   // surrounding whitespace ignored
    [InlineData("alpha media", "Alpha Media", false)]      // case-sensitive
    [InlineData("Alpha", "Alpha Media", false)]            // partial name is not enough
    [InlineData("Alpha  Media", "Alpha Media", false)]     // inner whitespace must match
    [InlineData("", "Alpha Media", false)]
    [InlineData("", "", false)]                            // never confirm against a blank name
    public void Matches_requires_exact_trimmed_name(string input, string name, bool expected) =>
        Assert.Equal(expected, DeleteConfirmation.Matches(input, name));
}
