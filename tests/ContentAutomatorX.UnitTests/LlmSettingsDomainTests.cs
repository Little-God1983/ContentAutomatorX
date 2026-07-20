using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.UnitTests;

public class LlmModelNameTests
{
    [Theory]
    [InlineData("opus")]
    [InlineData("sonnet")]
    [InlineData("haiku")]
    [InlineData("fable")]
    [InlineData("claude-opus-4-8")]
    [InlineData("claude-opus-4-8[1m]")]
    [InlineData("some.model_v2")]
    public void Accepts_real_aliases_and_full_ids(string model) =>
        Assert.True(LlmModelName.IsValid(model));

    [Theory]
    [InlineData("opus --dangerously-skip-permissions")]  // the attack this rule exists for
    [InlineData("opus sonnet")]
    [InlineData("\"opus\"")]
    [InlineData("opus;rm -rf /")]
    [InlineData("opus&whoami")]
    [InlineData("opus|tee x")]
    [InlineData("opus>out.txt")]
    [InlineData("opus%PATH%")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_anything_that_could_inject_an_argument(string? model) =>
        Assert.False(LlmModelName.IsValid(model));

    [Fact]
    public void Rejects_strings_over_the_length_cap()
    {
        Assert.True(LlmModelName.IsValid(new string('a', 100)));
        Assert.False(LlmModelName.IsValid(new string('a', 101)));
    }
}

public class LlmSettingsTests
{
    [Fact]
    public void Inherit_means_both_flags_omitted()
    {
        Assert.Equal("", LlmSettings.Inherit.Model);
        Assert.Equal(LlmEffort.Default, LlmSettings.Inherit.Effort);
    }

    [Theory]
    [InlineData("low", LlmEffort.Low)]
    [InlineData("medium", LlmEffort.Medium)]
    [InlineData("high", LlmEffort.High)]
    [InlineData("xhigh", LlmEffort.XHigh)]
    [InlineData("max", LlmEffort.Max)]
    [InlineData("HIGH", LlmEffort.High)]     // case-insensitive
    [InlineData("  high  ", LlmEffort.High)] // tolerates stored whitespace
    public void Parses_every_stored_effort_string(string stored, LlmEffort expected) =>
        Assert.Equal(expected, LlmSettings.ParseEffort(stored));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ludicrous")]   // hand-edited or written by a future version
    public void Unrecognized_effort_degrades_to_Default_rather_than_throwing(string? stored) =>
        Assert.Equal(LlmEffort.Default, LlmSettings.ParseEffort(stored));

    [Theory]
    [InlineData(LlmEffort.Default, "")]
    [InlineData(LlmEffort.Low, "low")]
    [InlineData(LlmEffort.XHigh, "xhigh")]
    [InlineData(LlmEffort.Max, "max")]
    public void Formats_effort_back_to_its_storage_string(LlmEffort effort, string expected) =>
        Assert.Equal(expected, LlmSettings.ToStorage(effort));

    [Fact]
    public void Storage_round_trips_for_every_enum_value()
    {
        foreach (var effort in Enum.GetValues<LlmEffort>())
            Assert.Equal(effort, LlmSettings.ParseEffort(LlmSettings.ToStorage(effort)));
    }

    [Fact]
    public void From_trims_the_model_and_parses_the_effort()
    {
        var settings = LlmSettings.From("  sonnet  ", "low");
        Assert.Equal("sonnet", settings.Model);
        Assert.Equal(LlmEffort.Low, settings.Effort);
    }

    [Fact]
    public void From_treats_null_or_blank_model_as_unset()
    {
        Assert.Equal("", LlmSettings.From(null, null).Model);
        Assert.Equal("", LlmSettings.From("   ", null).Model);
    }
}
