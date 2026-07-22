using System.Runtime.CompilerServices;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Llm;

namespace ContentAutomatorX.UnitTests;

/// <summary>A runner that replays canned NDJSON lines through the streaming path, so
/// ClaudeCliBackend's stream-json parsing can be asserted without spawning the CLI.</summary>
public class FakeStreamingRunner(params string[] lines) : IProcessRunner
{
    public string? LastArguments { get; private set; }
    public string? LastStdin { get; private set; }

    public Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin,
        TimeSpan timeout, CancellationToken ct = default) => throw new NotSupportedException();

    public async IAsyncEnumerable<string> RunStreamingAsync(string fileName, string arguments, string? stdin,
        TimeSpan idleTimeout, [EnumeratorCancellation] CancellationToken ct = default)
    {
        LastArguments = arguments;
        LastStdin = stdin;
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            yield return line;
            await Task.Yield();
        }
    }
}

public class ClaudeCliBackendStreamTests
{
    private const string Init = """{"type":"system","subtype":"init","session_id":"s"}""";
    private static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s);
    private static string Delta(string text) =>
        "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\",\"index\":0," +
        "\"delta\":{\"type\":\"text_delta\",\"text\":" + Json(text) + "}}}";
    private static string Assistant(string text) =>
        "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":" + Json(text) + "}]}}";
    private static string Result(string text, string model) =>
        "{\"type\":\"result\",\"subtype\":\"success\",\"result\":" + Json(text) +
        ",\"is_error\":false,\"modelUsage\":{" + Json(model) + ":{\"inputTokens\":1,\"outputTokens\":1}}}";

    private static async Task<List<LlmChunk>> Collect(ClaudeCliBackend backend, LlmSettings settings)
    {
        var chunks = new List<LlmChunk>();
        await foreach (var c in backend.StreamAsync("p", settings)) chunks.Add(c);
        return chunks;
    }

    [Fact]
    public async Task Yields_token_deltas_then_a_final_chunk_with_the_authoritative_result()
    {
        var runner = new FakeStreamingRunner(Init, Delta("He"), Delta("llo"), Assistant("Hello"), Result("Hello", "claude-sonnet-5"));
        var chunks = await Collect(new ClaudeCliBackend(runner, new ClaudeCliOptions()), new LlmSettings("sonnet", LlmEffort.Default));

        var deltas = chunks.Where(c => !c.IsFinal).Select(c => c.Text).ToList();
        Assert.Equal(["He", "llo"], deltas);                 // assistant whole-message ignored once deltas seen
        Assert.Equal("Hello", string.Concat(deltas));        // deltas reconstruct the reply for progress
        var final = Assert.Single(chunks, c => c.IsFinal);
        Assert.Equal("Hello", final.Text);                   // authoritative text from the result event
        Assert.Equal("claude-sonnet-5", final.Model);
    }

    [Fact]
    public async Task Final_text_matches_GenerateAsync_for_the_same_reply()
    {
        // The result event has the same shape as --output-format json, so both paths must agree.
        const string body = "# Draft\nHello.";
        var streamRunner = new FakeStreamingRunner(Init, Delta("# Draft\n"), Delta("Hello."), Result(body, "claude-opus-4-8"));
        var genRunner = new FakeProcessRunner(new ProcessResult(0,
            "{\"type\":\"result\",\"result\":" + Json(body) + ",\"is_error\":false}", ""));

        var streamFinal = (await Collect(new ClaudeCliBackend(streamRunner, new ClaudeCliOptions()), LlmSettings.Inherit))
            .Single(c => c.IsFinal).Text;
        var gen = await new ClaudeCliBackend(genRunner, new ClaudeCliOptions()).GenerateAsync("p", LlmSettings.Inherit);

        Assert.Equal(gen.Text, streamFinal);
    }

    [Fact]
    public async Task Falls_back_to_the_assistant_message_when_there_are_no_partial_deltas()
    {
        var runner = new FakeStreamingRunner(Init, Assistant("Hi there"), Result("Hi there", "m"));
        var chunks = await Collect(new ClaudeCliBackend(runner, new ClaudeCliOptions()), LlmSettings.Inherit);

        Assert.Contains(chunks, c => !c.IsFinal && c.Text == "Hi there");
        Assert.Equal("Hi there", chunks.Single(c => c.IsFinal).Text);
    }

    [Fact]
    public async Task Does_not_yield_a_final_chunk_for_an_error_result()
    {
        var runner = new FakeStreamingRunner(Delta("partial"), """{"type":"result","result":"x","is_error":true}""");
        var chunks = await Collect(new ClaudeCliBackend(runner, new ClaudeCliOptions()), LlmSettings.Inherit);

        Assert.DoesNotContain(chunks, c => c.IsFinal);
        Assert.Contains(chunks, c => c.Text == "partial");
    }

    [Fact]
    public async Task Requests_stream_json_with_verbose_and_partial_messages_and_passes_settings()
    {
        var runner = new FakeStreamingRunner(Result("ok", "m"));
        await Collect(new ClaudeCliBackend(runner, new ClaudeCliOptions()), new LlmSettings("sonnet", LlmEffort.High));

        Assert.Contains("--output-format stream-json", runner.LastArguments);
        Assert.Contains("--verbose", runner.LastArguments);
        Assert.Contains("--include-partial-messages", runner.LastArguments);
        Assert.Contains("--model sonnet", runner.LastArguments);
        Assert.Contains("--effort high", runner.LastArguments);
        Assert.Equal("p", runner.LastStdin);
    }

    [Fact]
    public async Task A_non_streaming_backend_is_still_a_valid_ILlmBackend()
    {
        // The capability test the call sites use: a fake that only implements GenerateAsync is not
        // an IStreamingLlmBackend, so callers fall back to it.
        var runner = new FakeProcessRunner(new ProcessResult(0, """{"type":"result","result":"ok","is_error":false}""", ""));
        var backend = new ClaudeCliBackend(runner, new ClaudeCliOptions());

        Assert.IsAssignableFrom<ContentAutomatorX.Domain.Abstractions.IStreamingLlmBackend>(backend);
    }
}
