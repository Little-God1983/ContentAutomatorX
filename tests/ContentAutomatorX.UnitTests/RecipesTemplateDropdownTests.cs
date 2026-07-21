namespace ContentAutomatorX.UnitTests;

/// <summary>Finding F1 — the newsletter-template dropdown's null entry used to read "Built-in
/// design", which is not what null means: NewsletterTemplateService.ResolveForPostAsync treats null
/// as "fall through to the tenant default", so once a tenant has any default template every recipe
/// showing that label silently renders in it, not the built-in design, the moment one is saved.
///
/// The first fix hardcoded "Tenant default (built-in if none)", which was accurate but described both
/// outcomes at once and so named neither — a recipe left on null still looked unconfigured while a
/// tenant default was set and in use. The label is now computed by DefaultTemplateLabel, which names
/// the template null actually resolves to and only mentions the built-in renderer when no default
/// exists. There is no bUnit harness in this codebase to render the component and assert on markup,
/// so this reads the .razor source directly — the same technique ReferenceTemplateTests uses for a
/// shipped file.</summary>
public class RecipesTemplateDropdownTests
{
    private static string Path() => System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "ContentAutomatorX.Web", "Components", "Pages", "Recipes.razor");

    private static string Source()
    {
        var path = Path();
        Assert.True(File.Exists(path), $"Recipes.razor not found at {System.IO.Path.GetFullPath(path)}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void The_null_template_option_states_what_null_actually_resolves_to()
    {
        var source = Source();

        Assert.DoesNotContain("Built-in design</MudSelectItem>", source);
        Assert.Contains("""Value="@((Guid?)null)">@DefaultTemplateLabel</MudSelectItem>""", source);
    }

    /// <summary>The label must name the tenant default when one exists — the whole point of the
    /// change. Pins both arms: a hardcoded string on either side would still satisfy the markup
    /// assertion above while telling the user nothing about what null resolves to.</summary>
    [Fact]
    public void The_null_option_label_names_the_default_template_and_falls_back_when_none_is_set()
    {
        var source = Source();

        Assert.Contains("""$"Tenant default ({d.Name})""", source);
        Assert.Contains("Tenant default (built-in — none set)", source);
    }

    /// <summary>Edit must reach the template the field names. Gating the button on
    /// _newsletterTemplateId left it disabled while the label named an editable tenant default, so
    /// the only way to open that template was to pin it to the recipe first.</summary>
    [Fact]
    public void The_edit_button_is_gated_on_the_resolved_template_not_the_raw_id()
    {
        var source = Source();

        Assert.DoesNotContain("""Disabled="@(_newsletterTemplateId is null)""", source);
        Assert.Contains("""Disabled="@(SelectedTemplate is null)""", source);
    }
}
