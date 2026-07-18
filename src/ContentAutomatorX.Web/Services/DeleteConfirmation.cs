namespace ContentAutomatorX.Web.Services;

public static class DeleteConfirmation
{
    /// <summary>Typed confirmation must equal the tenant name: case-sensitive, surrounding whitespace ignored.</summary>
    public static bool Matches(string input, string tenantName) =>
        !string.IsNullOrWhiteSpace(tenantName)
        && string.Equals(input.Trim(), tenantName.Trim(), StringComparison.Ordinal);
}
