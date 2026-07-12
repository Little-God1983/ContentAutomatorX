using ContentAutomatorX.Infrastructure.Llm;

namespace ContentAutomatorX.UnitTests;

public class FakeProcessRunner(params ProcessResult[] results) : IProcessRunner
{
    public int Calls { get; private set; }
    public string? LastStdin { get; private set; }
    public string? LastArguments { get; private set; }

    public Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin,
        TimeSpan timeout, CancellationToken ct = default)
    {
        LastStdin = stdin;
        LastArguments = arguments;
        var result = results[Math.Min(Calls, results.Length - 1)];
        Calls++;
        return Task.FromResult(result);
    }
}

public class ClaudeCliBackendTests
{
    private const string GoodJson = """{"type":"result","result":"# Draft\nHello.","is_error":false}""";

    [Fact]
    public async Task Returns_result_text_and_pipes_prompt_via_stdin()
    {
        var runner = new FakeProcessRunner(new ProcessResult(0, GoodJson, ""));
        var backend = new ClaudeCliBackend(runner, new ClaudeCliOptions { Model = "claude-sonnet-5" });

        var result = await backend.GenerateAsync("write things");

        Assert.StartsWith("# Draft", result.Text);
        Assert.Equal("write things", runner.LastStdin);
        Assert.Contains("--output-format json", runner.LastArguments);
        Assert.Contains("--model claude-sonnet-5", runner.LastArguments);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task Retries_once_then_succeeds()
    {
        var runner = new FakeProcessRunner(
            new ProcessResult(1, "", "transient failure"),
            new ProcessResult(0, GoodJson, ""));
        var backend = new ClaudeCliBackend(runner, new ClaudeCliOptions());

        var result = await backend.GenerateAsync("p");

        Assert.StartsWith("# Draft", result.Text);
        Assert.Equal(2, runner.Calls);
    }

    [Fact]
    public async Task Fails_after_two_attempts()
    {
        var runner = new FakeProcessRunner(new ProcessResult(1, "", "dead"));
        var backend = new ClaudeCliBackend(runner, new ClaudeCliOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.GenerateAsync("p"));
        Assert.Equal(2, runner.Calls);
    }
}
