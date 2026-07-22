using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

/// <summary>An optional capability on top of <see cref="ILlmBackend"/>: emitting a response
/// incrementally as it is generated, rather than only after it completes.
///
/// <para>Kept separate from <see cref="ILlmBackend"/> on purpose. A backend that cannot stream (a
/// future LM Studio or Ollama path, or a test double) is still a perfectly valid
/// <see cref="ILlmBackend"/>; callers that want a stream test for this capability
/// (<c>llm is IStreamingLlmBackend</c>) and fall back to <see cref="ILlmBackend.GenerateAsync"/>
/// otherwise. This preserves the provider-neutrality the model-selector work established.</para>
///
/// <para>Structured-output callers (chat, topic blurbs) must still parse only the complete
/// accumulated reply — partial JSON is not parseable — and so use the stream for progress display
/// only, reading the final result from the terminal <see cref="LlmChunk"/>.</para>
/// </summary>
public interface IStreamingLlmBackend : ILlmBackend
{
    /// <summary>Streams the reply as <see cref="LlmChunk"/>s. The last chunk has
    /// <see cref="LlmChunk.IsFinal"/> set and carries the complete text and model. Cancelling
    /// <paramref name="ct"/> (or abandoning the enumeration) terminates the underlying work.</summary>
    IAsyncEnumerable<LlmChunk> StreamAsync(string prompt, LlmSettings settings, CancellationToken ct = default);
}
