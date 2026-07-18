namespace ContentAutomatorX.Domain;

public static class SourceTypes
{
    public const string Reddit = "Reddit";
    public const string Rss = "Rss";
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
