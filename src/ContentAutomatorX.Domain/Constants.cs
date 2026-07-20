namespace ContentAutomatorX.Domain;

public static class SourceTypes
{
    public const string Reddit = "Reddit";
    public const string Rss = "Rss";
    public const string Website = "Website";
    public const string LlmResearch = "LlmResearch";
}

public static class DraftKinds
{
    public const string Newsletter = "Newsletter";
    public const string SocialPost = "SocialPost";
    public const string VideoScript = "VideoScript";
    public static readonly string[] All = [Newsletter, SocialPost, VideoScript];
}

public static class RunKinds
{
    public const string Ingestion = "Ingestion";
    public const string Generation = "Generation";
}

public static class RunTriggers
{
    public const string Scheduled = "Scheduled";
    public const string Manual = "Manual";
    public const string Mcp = "Mcp";
}

public static class PlatformTypes
{
    public const string MailerLite = "MailerLite";
}

public static class SectionTypes
{
    public const string Header = "Header";
    public const string Topic = "Topic";
    public const string Sponsor = "Sponsor";
    public const string Button = "Button";
    public const string Divider = "Divider";
    public const string Footer = "Footer";
    public const string LegacyBody = "LegacyBody";
}

public static class ChatRoles
{
    public const string User = "User";
    public const string Assistant = "Assistant";
}

public static class RevisionStacks
{
    public const string Undo = "Undo";
    public const string Redo = "Redo";
}
