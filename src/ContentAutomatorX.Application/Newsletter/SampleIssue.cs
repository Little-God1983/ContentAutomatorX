using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>The fixed issue the template editor previews against. Deliberately exercises every
/// block and both sides of every IF region — the entry at position 3 has nothing optional set, so a template
/// author sees immediately whether their regions collapse cleanly. A real issue would leave
/// whichever blocks it happens not to use untested while they are being edited.</summary>
public static class SampleIssue
{
    private static readonly Guid PostId = new("00000000-0000-0000-0000-0000000005a3");

    public static Tenant Tenant { get; } = new()
    {
        Name = "Into the Latent",
        Slug = "sample",
        SenderIdentity = "Your Name · c/o Your Address · 00000 Your City · Country",
        BrandingJson = """{"accentColorHex":"#1AE6D5"}"""
    };

    public static IReadOnlyList<IssueSection> Sections { get; } =
    [
        Make(0, SectionTypes.Header, title: "Signals from the latent space",
            body: "Hey — this month was mostly about consistency: getting a face to survive 50 "
                + "generations without drifting. Two write-ups below, plus a new build video."),
        Make(1, SectionTypes.Topic, title: "Training a Flux LoRA that actually holds a face",
            body: "Caption strategy, learning-rate schedule, and the regularisation set that stopped "
                + "my character drifting after twenty generations.",
            image: "https://placehold.co/1072x600/0F0F1A/1AE6D5/png?text=Cover+image",
            link: "https://example.com/blog/flux-lora-consistency", category: "Tutorial"),
        Make(2, SectionTypes.Divider),
        // Nothing optional set: this is the one that proves IF regions collapse.
        Make(3, SectionTypes.Topic, title: "A shorter note with no cover image",
            body: "No image, no link, no category — everything optional is absent here on purpose."),
        Make(4, SectionTypes.Video, title: "Building a character-consistent pipeline, end to end",
            body: "42 minutes, no cuts, including the parts that broke.",
            link: "https://www.youtube.com/watch?v=dQw4w9WgXcQ", linkText: "Watch the build →"),
        Make(5, SectionTypes.Sponsor, title: "Sponsor name — one-line pitch goes here",
            body: "Two or three sentences of sponsor copy. Keeps the same rhythm as the rest of the "
                + "issue, but the label and the tinted panel make it unmistakably paid placement.",
            image: "https://placehold.co/200x60/EAEBF1/5A5E70/png?text=Logo",
            link: "https://example.com", linkText: "Visit sponsor →"),
        Make(6, SectionTypes.Button, link: "https://example.com/services", linkText: "See the services"),
        Make(7, SectionTypes.Footer,
            body: "You're receiving this because you subscribed at example.com.")
    ];

    private static IssueSection Make(int position, string type, string? title = null,
        string? body = null, string? image = null, string? link = null, string? linkText = null,
        string? category = null) =>
        new()
        {
            // Stable ids: the preview re-renders on every keystroke and must not churn.
            Id = new Guid($"00000000-0000-0000-0000-0000000000{position:d2}"),
            PostId = PostId, Position = position, Type = type, Title = title, BodyMd = body,
            ImageUrl = image, LinkUrl = link, LinkText = linkText, Category = category
        };
}
