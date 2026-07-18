using System.Text;

namespace ContentAutomatorX.Web.Services;

public static class TenantSlug
{
    /// <summary>Derives a slug: lowercase, ASCII letters/digits kept, space/-/_ become one hyphen, rest dropped.</summary>
    public static string Derive(string name)
    {
        var sb = new StringBuilder(name.Length);
        var lastWasHyphen = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasHyphen = false;
            }
            else if (ch is ' ' or '-' or '_' && sb.Length > 0 && !lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }
        return sb.ToString().TrimEnd('-');
    }
}
