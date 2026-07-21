namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Finds HTML comment spans in text using the rule a real HTML parser applies, not the
/// stricter one a regex naturally encodes. A comment starts at "&lt;!--" and ends at the first
/// "--&gt;" that follows it — OR at end of text if no "--&gt;" ever appears. That second case is
/// HTML's "eof-in-comment" behaviour: every browser and email client treats an unterminated
/// "&lt;!--" as swallowing the remainder of the document, not just the well-formed span a
/// "&lt;!--.*?--&gt;" regex would match. A regex-based scan is invisible to exactly that case,
/// which is what let a mistyped or edited-out closing "--&gt;" hide a genuine unsubscribe link (and
/// everything after it) from every reader while looking, to the validator, like ordinary text.
///
/// One linear left-to-right scan, shared by TemplateValidator (save-time gate) and
/// TemplateHtmlRenderer (render-time backstop) so the two independent HTML-comment-awareness checks
/// — which must agree on exactly what counts as "hidden inside a comment" — cannot drift apart the
/// way two hand-maintained copies of the same regex eventually did.
///
/// Deliberately has no knowledge of "&lt;!-- BLOCK: … --&gt;" or "&lt;!-- IF: … --&gt;" markers.
/// Both are themselves ordinary, well-formed HTML comments (they open with "&lt;!--" and close at
/// their own first "--&gt;"), so a generic scan naturally treats a marker as a short, self-closing
/// comment without needing to recognise it as a marker at all — there is no separate "marker
/// exclusion" step whose whitespace- or casing-sensitive recognition logic could itself be fooled
/// or exploited to swallow more text than the marker's own span.</summary>
public static class CommentScanner
{
    /// <summary>One HTML comment span, [Start, End). Terminated is false only for the last span a
    /// scan can produce — the text ran out before a closing "--&gt;" was found — in which case End
    /// equals the text's length, since eof-in-comment means everything from Start to the end of the
    /// text is inside that one comment.</summary>
    public readonly record struct CommentSpan(int Start, int End, bool Terminated);

    public static IReadOnlyList<CommentSpan> Find(string text)
    {
        var spans = new List<CommentSpan>();
        var i = 0;
        while (true)
        {
            var start = text.IndexOf("<!--", i, StringComparison.Ordinal);
            if (start < 0) break;

            var closeAt = text.IndexOf("-->", start + 4, StringComparison.Ordinal);
            if (closeAt < 0)
            {
                // eof-in-comment: nothing beyond this point can start a new comment of its own —
                // it is all already inside this one, so the scan stops here.
                spans.Add(new CommentSpan(start, text.Length, false));
                break;
            }

            spans.Add(new CommentSpan(start, closeAt + 3, true));
            i = closeAt + 3;
        }
        return spans;
    }

    /// <summary>True when index falls inside any comment span, terminated or not.</summary>
    public static bool IsInside(IReadOnlyList<CommentSpan> comments, int index) =>
        comments.Any(c => index >= c.Start && index < c.End);
}
