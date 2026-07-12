namespace ContentAutomatorX.Domain.Models;

public class SelectionRules
{
    public int? TimeWindowDays { get; set; }
    public int? MinScore { get; set; }
    public int MaxItems { get; set; } = 10;
    public string[] IncludeKeywords { get; set; } = [];
    public string[] ExcludeKeywords { get; set; } = [];
}
