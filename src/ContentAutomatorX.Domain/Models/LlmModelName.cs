using System.Text.RegularExpressions;

namespace ContentAutomatorX.Domain.Models;

/// <summary>The single rule for what may be used as a model name.
///
/// This exists because the model string becomes an argument on a process the app
/// spawns: "opus --dangerously-skip-permissions" would otherwise inject a flag
/// into the CLI call. On Windows ProcessRunner may additionally route through
/// cmd.exe /c for npm shims (see WindowsCommandResolver), putting a second
/// parser on the path — so shell metacharacters are rejected too.
///
/// Lives in Domain so the service and the UI enforce the same rule; the service
/// is the authority (the UI can be bypassed, the service cannot).</summary>
public static partial class LlmModelName
{
    public const int MaxLength = 100;

    [GeneratedRegex(@"^[A-Za-z0-9._\-\[\]]+$")]
    private static partial Regex Allowed();

    /// <summary>True for a usable model name. Blank is NOT valid here — callers
    /// treat blank as "unset" and skip validation entirely.</summary>
    public static bool IsValid(string? model) =>
        !string.IsNullOrEmpty(model) && model.Length <= MaxLength && Allowed().IsMatch(model);
}
