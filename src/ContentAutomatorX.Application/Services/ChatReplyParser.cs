using System.Text.Json;

namespace ContentAutomatorX.Application.Services;

/// <summary>One proposed rewrite of one existing section. A null Title or BodyMd means that field
/// is unchanged — the model is not required to restate what it is not touching.</summary>
public record ChatEdit(Guid SectionId, string? Title, string? BodyMd);

/// <summary>What the model said, plus what it wants to change. DroppedEdits counts edits that were
/// structurally unusable, so the UI can say so instead of quietly proposing fewer changes.</summary>
public record ChatReply(string Reply, IReadOnlyList<ChatEdit> Edits, int DroppedEdits);

public static class ChatReplyParser
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private record RawEdit(string? SectionId, string? Title, string? BodyMd);
    private record RawReply(string? Reply, List<RawEdit>? Edits);

    /// <summary>Structural validation only. Whether a sectionId actually belongs to the issue is
    /// the service's business — the parser has never heard of the issue.</summary>
    public static bool TryParse(string text, out ChatReply? reply)
    {
        reply = null;
        try
        {
            var raw = JsonSerializer.Deserialize<RawReply>(MarkdownFence.Strip(text), JsonOpts);
            if (raw is null) return false;

            var edits = new List<ChatEdit>();
            var dropped = 0;
            foreach (var edit in raw.Edits ?? [])
            {
                var hasField = !string.IsNullOrWhiteSpace(edit.Title) || !string.IsNullOrWhiteSpace(edit.BodyMd);
                // Parsed as a string, not a Guid: a Guid-typed property throws during Deserialize,
                // which would lose every other edit in the same reply. Models really do emit junk
                // here — echoing the prompt's own "<id>" placeholder is a common habit.
                if (!Guid.TryParse(edit.SectionId, out var sectionId) || sectionId == Guid.Empty || !hasField)
                {
                    dropped++;
                    continue;
                }
                edits.Add(new ChatEdit(sectionId, NullIfBlank(edit.Title), NullIfBlank(edit.BodyMd)));
            }

            var prose = raw.Reply?.Trim() ?? "";
            // A turn that neither says anything nor proposes anything is a failed turn, not an
            // empty success — the caller should retry rather than show a blank bubble.
            if (prose.Length == 0 && edits.Count == 0) return false;

            reply = new ChatReply(prose, edits, dropped);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
