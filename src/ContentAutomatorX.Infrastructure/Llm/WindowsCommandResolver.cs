using System.IO;

namespace ContentAutomatorX.Infrastructure.Llm;

/// <summary>
/// Resolves a bare command (e.g. "claude") to a launchable (fileName, arguments)
/// pair the way a Windows shell would. npm and many tools install CLIs as
/// <c>.cmd</c>/<c>.bat</c> shims — which <see cref="System.Diagnostics.Process"/>
/// (with UseShellExecute=false) cannot launch directly, and which it never finds
/// on PATH because a raw CreateProcess only resolves real executables (.exe).
/// When the resolved command is a batch shim we route it through <c>cmd.exe /c</c>
/// using the outer-quote form that survives spaces in the resolved path.
/// Pure/string-only so it is deterministically unit-testable on any OS; the
/// Windows-only gate lives in <see cref="ProcessRunner"/>.
/// </summary>
public static class WindowsCommandResolver
{
    private static readonly string[] BatchExtensions = [".cmd", ".bat"];

    /// <param name="command">Command as configured (bare name, or a path).</param>
    /// <param name="arguments">CLI arguments, already assembled.</param>
    /// <param name="fileExists">Probe for a full candidate path (injected for tests).</param>
    /// <param name="pathDirs">Directories from %PATH%, in order.</param>
    /// <param name="pathExts">Extensions from %PATHEXT% (e.g. .COM;.EXE;.BAT;.CMD), in order.</param>
    public static (string FileName, string Arguments) Resolve(
        string command, string arguments,
        Func<string, bool> fileExists,
        IEnumerable<string> pathDirs,
        IEnumerable<string> pathExts)
    {
        var resolved = ResolvePath(command, fileExists, pathDirs, pathExts);
        if (resolved is null)
            return (command, arguments); // not found — let Process.Start fail with its clear error

        // .exe/.com run directly; batch shims must go through the command interpreter.
        // cmd's /c quoting rule: if the whole tail is wrapped in one extra pair of
        // quotes, cmd strips that pair and runs the rest verbatim — so the inner
        // quotes keep a space in the path (C:\Users\Little God\...) intact.
        return IsBatch(resolved)
            ? ("cmd.exe", $"/c \"\"{resolved}\" {arguments}\"")
            : (resolved, arguments);
    }

    private static string? ResolvePath(
        string command, Func<string, bool> fileExists,
        IEnumerable<string> pathDirs, IEnumerable<string> pathExts)
    {
        var exts = pathExts as IReadOnlyList<string> ?? pathExts.ToList();

        // Explicit path (rooted or containing a separator): probe as given, then with each PATHEXT.
        if (command.Contains('\\') || command.Contains('/'))
            return ProbeCandidates(command, fileExists, exts);

        // Bare name: search each PATH directory, exact-then-PATHEXT.
        foreach (var dir in pathDirs)
        {
            var candidate = dir.TrimEnd('\\', '/') + "\\" + command;
            if (ProbeCandidates(candidate, fileExists, exts) is { } hit)
                return hit;
        }
        return null;
    }

    private static string? ProbeCandidates(string basePath, Func<string, bool> fileExists, IReadOnlyList<string> exts)
    {
        if (Path.HasExtension(basePath) && fileExists(basePath))
            return basePath;
        // %PATHEXT% is conventionally uppercase; append it lowercased so the resolved
        // path reads naturally in logs (the filesystem match is case-insensitive anyway).
        foreach (var ext in exts)
        {
            var candidate = basePath + ext.ToLowerInvariant();
            if (fileExists(candidate))
                return candidate;
        }
        return null;
    }

    private static bool IsBatch(string path) =>
        BatchExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
