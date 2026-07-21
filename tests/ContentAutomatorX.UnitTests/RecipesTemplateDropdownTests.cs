namespace ContentAutomatorX.UnitTests;

/// <summary>Finding F1 — the newsletter-template dropdown's null entry used to read "Built-in
/// design", which is not what null means: NewsletterTemplateService.ResolveForPostAsync treats null
/// as "fall through to the tenant default", so once a tenant has any default template every recipe
/// showing that label silently renders in it, not the built-in design, the moment one is saved. There
/// is no bUnit harness in this codebase to render the component and assert on markup, so this reads
/// the .razor source directly — the same technique ReferenceTemplateTests uses for a shipped file —
/// and asserts the misleading label is gone and the corrected one is present.</summary>
public class RecipesTemplateDropdownTests
{
    private static string Path() => System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "ContentAutomatorX.Web", "Components", "Pages", "Recipes.razor");

    [Fact]
    public void The_null_template_option_states_what_null_actually_resolves_to()
    {
        var path = Path();
        Assert.True(File.Exists(path), $"Recipes.razor not found at {System.IO.Path.GetFullPath(path)}");
        var source = File.ReadAllText(path);

        Assert.DoesNotContain("Built-in design</MudSelectItem>", source);
        Assert.Contains("""Value="@((Guid?)null)">Tenant default (built-in if none)</MudSelectItem>""", source);
    }
}
