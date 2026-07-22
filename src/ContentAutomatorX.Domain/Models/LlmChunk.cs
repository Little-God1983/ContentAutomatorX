namespace ContentAutomatorX.Domain.Models;

/// <summary>One increment of a streaming LLM response.
///
/// <para>Deliberately minimal — a text delta, plus a terminal marker. No token counts or stop
/// reason: that matches <see cref="LlmResult"/>'s existing no-usage design; add it as its own change
/// if a UI ever needs it.</para>
///
/// <para>The <see cref="IsFinal"/> chunk carries the <b>authoritative complete text</b> (the same
/// value <c>GenerateAsync</c> would return for the prompt) and the resolved <see cref="Model"/>.
/// Non-final chunks are progress deltas whose concatenation approximates the reply but is not relied
/// upon for correctness — a consumer that needs the exact result uses the final chunk's text.</para>
/// </summary>
/// <param name="Text">A text delta (non-final) or the complete reply text (final).</param>
/// <param name="IsFinal">True for the single terminal chunk that closes the stream.</param>
/// <param name="Model">The model that produced the reply — set on the final chunk.</param>
public record LlmChunk(string Text, bool IsFinal = false, string? Model = null);
