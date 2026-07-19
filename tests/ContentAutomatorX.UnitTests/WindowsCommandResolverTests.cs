using ContentAutomatorX.Infrastructure.Llm;

namespace ContentAutomatorX.UnitTests;

public class WindowsCommandResolverTests
{
    // Mirrors a real machine: npm ships "claude" (extensionless bash script), "claude.cmd",
    // and "claude.ps1" — but no claude.exe.
    private const string NpmDir = @"C:\Users\Little God\AppData\Roaming\npm";
    private const string SystemDir = @"C:\Windows\System32";

    private static readonly string[] PathExts = [".COM", ".EXE", ".BAT", ".CMD", ".PS1"];

    private static Func<string, bool> Files(params string[] existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Fact]
    public void Bare_command_resolving_to_cmd_shim_is_routed_through_cmd_exe()
    {
        var shim = NpmDir + @"\claude.cmd";
        var files = Files(NpmDir + @"\claude", shim, NpmDir + @"\claude.ps1");

        var (fileName, arguments) = WindowsCommandResolver.Resolve(
            "claude", "-p --output-format json", files, [NpmDir], PathExts);

        Assert.Equal("cmd.exe", fileName);
        // Extra outer quotes so cmd strips them and the inner quotes preserve the space in the path.
        Assert.Equal($"/c \"\"{shim}\" -p --output-format json\"", arguments);
    }

    [Fact]
    public void Exe_on_path_wins_over_later_batch_and_launches_directly()
    {
        var exe = NpmDir + @"\claude.exe";
        var files = Files(exe, NpmDir + @"\claude.cmd");

        var (fileName, arguments) = WindowsCommandResolver.Resolve(
            "claude", "-p", files, [NpmDir], PathExts);

        Assert.Equal(exe, fileName);
        Assert.Equal("-p", arguments); // untouched — no cmd.exe wrapping
    }

    [Fact]
    public void Earlier_path_directory_wins()
    {
        var systemExe = SystemDir + @"\claude.exe";
        var files = Files(systemExe, NpmDir + @"\claude.cmd");

        var (fileName, _) = WindowsCommandResolver.Resolve(
            "claude", "-p", files, [SystemDir, NpmDir], PathExts);

        Assert.Equal(systemExe, fileName);
    }

    [Fact]
    public void Unresolvable_command_is_returned_unchanged()
    {
        var (fileName, arguments) = WindowsCommandResolver.Resolve(
            "claude", "-p", Files(), [NpmDir], PathExts);

        Assert.Equal("claude", fileName);
        Assert.Equal("-p", arguments);
    }

    [Fact]
    public void Explicit_full_path_to_a_shim_is_still_routed_through_cmd_exe()
    {
        var shim = NpmDir + @"\claude.cmd";

        var (fileName, arguments) = WindowsCommandResolver.Resolve(
            shim, "-p", Files(shim), [], PathExts);

        Assert.Equal("cmd.exe", fileName);
        Assert.Equal($"/c \"\"{shim}\" -p\"", arguments);
    }

    [Fact]
    public void Explicit_full_path_to_an_exe_launches_directly()
    {
        var exe = NpmDir + @"\claude.exe";

        var (fileName, arguments) = WindowsCommandResolver.Resolve(
            exe, "-p", Files(exe), [], PathExts);

        Assert.Equal(exe, fileName);
        Assert.Equal("-p", arguments);
    }
}
