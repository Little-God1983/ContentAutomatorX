using ContentAutomatorX.Domain;

namespace ContentAutomatorX.Application.Generation;

public static class DefaultTemplates
{
    public const string Newsletter = """
        You are writing a newsletter issue for a content creator.

        Voice and audience:
        {voice_profile}

        Style directives:
        {tone_modifiers}

        Write a complete newsletter in Markdown based on the source items below.
        Structure: a short personal intro, 3-5 topic sections (each with a heading,
        a summary in the creator's voice, and the source link), and a brief outro
        with a call to action. Do not invent facts not present in the items.

        Source items:
        {items}

        Additional instructions: {extra_instructions}

        Output ONLY the newsletter Markdown, starting with a # title line.
        """;

    public const string SocialPost = """
        You are writing a social media post (e.g. Patreon or Ko-fi update) for a content creator.

        Voice and audience:
        {voice_profile}

        Style directives:
        {tone_modifiers}

        Write ONE engaging post in Markdown based on the source items below.
        Keep it punchy: a hook line, 2-4 short paragraphs or bullets, and a
        closing question or call to action. Include at most 2 links.
        Do not invent facts not present in the items.

        Source items:
        {items}

        Additional instructions: {extra_instructions}

        Output ONLY the post Markdown, starting with a # title line.
        """;

    public const string VideoScript = """
        You are writing a YouTube video script for a content creator.

        Voice and audience:
        {voice_profile}

        Style directives:
        {tone_modifiers}

        Write a complete video script in Markdown based on the source items below.
        Structure with these headings: ## Hook (first 15 seconds), ## Intro,
        ## Section 1..N (one per major topic, with spoken narration text),
        ## Outro, ## CTA. Mark visual/B-roll suggestions as blockquotes.
        Do not invent facts not present in the items.

        Source items:
        {items}

        Additional instructions: {extra_instructions}

        Output ONLY the script Markdown, starting with a # title line.
        """;

    public static string GetFor(string kind) => kind switch
    {
        DraftKinds.Newsletter => Newsletter,
        DraftKinds.SocialPost => SocialPost,
        DraftKinds.VideoScript => VideoScript,
        _ => throw new ArgumentException($"No default template for kind '{kind}'", nameof(kind))
    };
}
