namespace ContentAutomatorX.UnitTests;

/// <summary>DPAPI-backed tests run only on Windows; elsewhere they skip instead of failing.</summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows (DPAPI)";
    }
}
