using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Llm;

namespace ContentAutomatorX.UnitTests;

/// <summary>Captures what the backend would launch, so the args string can be
/// asserted exactly without spawning a process.</summary>
public class RecordingRunner(string stdout = """{"result":"ok"}""") : IProcessRunner
{
    public string? LastArguments { get; private set; }
    public string? LastFileName { get; private set; }

    public Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin,
        TimeSpan timeout, CancellationToken ct = default)
    {
        LastFileName = fileName;
        LastArguments = arguments;
        return Task.FromResult(new ProcessResult(0, stdout, ""));
    }
}

public class ClaudeCliBackendArgsTests
{
    private static async Task<string> ArgsFor(LlmSettings settings, ClaudeCliOptions? options = null)
    {
        var runner = new RecordingRunner();
        var backend = new ClaudeCliBackend(runner, options ?? new ClaudeCliOptions());
        await backend.GenerateAsync("hello", settings);
        return runner.LastArguments!;
    }

    [Fact]
    public async Task Inherit_produces_exactly_todays_arguments() =>
        Assert.Equal("-p --output-format json", await ArgsFor(LlmSettings.Inherit));

    [Fact]
    public async Task Model_only_adds_the_model_flag() =>
        Assert.Equal("-p --output-format json --model sonnet",
            await ArgsFor(new LlmSettings("sonnet", LlmEffort.Default)));

    [Fact]
    public async Task Effort_only_adds_the_effort_flag() =>
        Assert.Equal("-p --output-format json --effort xhigh",
            await ArgsFor(new LlmSettings("", LlmEffort.XHigh)));

    [Fact]
    public async Task Both_set_adds_both_flags_model_first() =>
        Assert.Equal("-p --output-format json --model claude-opus-4-8 --effort max",
            await ArgsFor(new LlmSettings("claude-opus-4-8", LlmEffort.Max)));

    [Fact]
    public async Task ExtraArgs_is_appended_after_the_flags()
    {
        var options = new ClaudeCliOptions { ExtraArgs = "--allowedTools WebSearch" };
        Assert.Equal("-p --output-format json --model haiku --effort low --allowedTools WebSearch",
            await ArgsFor(new LlmSettings("haiku", LlmEffort.Low), options));
    }

    [Fact]
    public async Task Passed_settings_beat_the_appsettings_model()
    {
        // The service already applied the fallback chain; whatever arrives here wins.
        var options = new ClaudeCliOptions { Model = "opus" };
        Assert.Equal("-p --output-format json --model sonnet",
            await ArgsFor(new LlmSettings("sonnet", LlmEffort.Default), options));
    }

    [Theory]
    [InlineData(LlmEffort.Low, "low")]
    [InlineData(LlmEffort.Medium, "medium")]
    [InlineData(LlmEffort.High, "high")]
    [InlineData(LlmEffort.XHigh, "xhigh")]
    [InlineData(LlmEffort.Max, "max")]
    public async Task Every_effort_level_maps_to_the_CLI_vocabulary(LlmEffort effort, string expected) =>
        Assert.Contains($"--effort {expected}", await ArgsFor(new LlmSettings("", effort)));

    [Fact]
    public async Task Reports_the_model_the_CLI_actually_used()
    {
        var runner = new RecordingRunner("""{"result":"ok","modelUsage":{"claude-sonnet-5":{}}}""");
        var backend = new ClaudeCliBackend(runner, new ClaudeCliOptions());

        var result = await backend.GenerateAsync("hi", new LlmSettings("sonnet", LlmEffort.Default));

        Assert.Equal("claude-sonnet-5", result.Model);
    }
}
