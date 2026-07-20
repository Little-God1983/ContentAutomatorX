using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class LlmSettingsServiceTests
{
    private static LlmFallbackSettings Fallback(LlmSettings settings) => new(settings);

    private static async Task<Tenant> SeedTenant(TestDb test, string slug)
    {
        var tenant = new Tenant { Name = slug, Slug = slug };
        test.Db.Tenants.Add(tenant);
        await test.Db.SaveChangesAsync();
        return tenant;
    }

    [Fact]
    public async Task Unconfigured_tenant_falls_back_to_appsettings()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(new LlmSettings("haiku", LlmEffort.Low)));

        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("haiku", settings.Model);
        Assert.Equal(LlmEffort.Low, settings.Effort);
    }

    [Fact]
    public async Task Unconfigured_tenant_with_blank_appsettings_inherits()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        Assert.Equal(LlmSettings.Inherit, await svc.GetAsync(tenant.Id));
    }

    [Fact]
    public async Task Saved_values_round_trip()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(tenant.Id, new LlmSettings("sonnet", LlmEffort.XHigh));
        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("sonnet", settings.Model);
        Assert.Equal(LlmEffort.XHigh, settings.Effort);
    }

    [Fact]
    public async Task Saving_twice_upserts_rather_than_adding_a_second_row()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(tenant.Id, new LlmSettings("opus", LlmEffort.High));
        await svc.SaveAsync(tenant.Id, new LlmSettings("haiku", LlmEffort.Low));

        Assert.Equal(1, await test.Db.TenantLlmSettings.CountAsync(s => s.TenantId == tenant.Id));
        Assert.Equal("haiku", (await svc.GetAsync(tenant.Id)).Model);
    }

    [Fact]
    public async Task Tenants_do_not_see_each_others_settings()
    {
        using var test = TestDb.Create();
        var a = await SeedTenant(test, "a");
        var b = await SeedTenant(test, "b");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(a.Id, new LlmSettings("opus", LlmEffort.Max));

        Assert.Equal("opus", (await svc.GetAsync(a.Id)).Model);
        Assert.Equal("", (await svc.GetAsync(b.Id)).Model);
        Assert.Equal(LlmEffort.Default, (await svc.GetAsync(b.Id)).Effort);
    }

    [Fact]
    public async Task Save_rejects_an_injected_flag_and_writes_nothing()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveAsync(tenant.Id, new LlmSettings("opus --dangerously-skip-permissions", LlmEffort.Default)));

        Assert.Equal(0, await test.Db.TenantLlmSettings.CountAsync());
    }

    [Fact]
    public async Task Save_accepts_a_blank_model_as_meaning_unset()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(tenant.Id, new LlmSettings("", LlmEffort.High));
        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("", settings.Model);
        Assert.Equal(LlmEffort.High, settings.Effort);
    }

    [Fact]
    public async Task A_garbage_effort_string_in_the_row_degrades_to_Default()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        test.Db.TenantLlmSettings.Add(new TenantLlmSetting
        {
            TenantId = tenant.Id, Model = "opus", Effort = "ludicrous"
        });
        await test.Db.SaveChangesAsync();
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("opus", settings.Model);
        Assert.Equal(LlmEffort.Default, settings.Effort);
    }

    [Fact]
    public async Task A_row_with_a_blank_model_still_falls_back_for_the_model_only()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(new LlmSettings("haiku", LlmEffort.Low)));

        await svc.SaveAsync(tenant.Id, new LlmSettings("", LlmEffort.Max));
        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("haiku", settings.Model);      // blank model -> appsettings
        Assert.Equal(LlmEffort.Max, settings.Effort); // explicit effort wins
    }

    /// <summary>In Blazor Server the scoped AppDbContext lives for the whole circuit,
    /// and each browser tab is its own circuit. A tracking read would let EF identity
    /// resolution hand this circuit the snapshot it already loaded, so a save in tab A
    /// would never reach a tab B that had read the row once.</summary>
    [Fact]
    public async Task GetAsync_reflects_a_save_made_through_another_context()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(tenant.Id, new LlmSettings("opus", LlmEffort.Low));
        Assert.Equal("opus", (await svc.GetAsync(tenant.Id)).Model);

        using (var other = test.NewContext())
            await new LlmSettingsService(other, Fallback(LlmSettings.Inherit))
                .SaveAsync(tenant.Id, new LlmSettings("haiku", LlmEffort.Max));

        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("haiku", settings.Model);
        Assert.Equal(LlmEffort.Max, settings.Effort);
    }

    /// <summary>A row can only hold an invalid model if it was hand-edited or written
    /// by something that bypassed SaveAsync. Degrade to "unset" like ParseEffort does
    /// rather than passing an unvalidated string into a process argument.</summary>
    [Fact]
    public async Task An_invalid_model_in_the_row_resolves_to_unset()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        test.Db.TenantLlmSettings.Add(new TenantLlmSetting
        {
            TenantId = tenant.Id, Model = "opus --dangerously-skip-permissions", Effort = "high"
        });
        await test.Db.SaveChangesAsync();
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("", settings.Model);
        Assert.Equal(LlmEffort.High, settings.Effort);
    }

    [Fact]
    public async Task An_invalid_appsettings_model_resolves_to_unset()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(new LlmSettings("opus & calc.exe", LlmEffort.Low)));

        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("", settings.Model);
        Assert.Equal(LlmEffort.Low, settings.Effort);
    }

    /// <summary>The editing UI binds to this, so it must show the tenant's own choice.
    /// If it applied the fallback, Save would silently promote an inherited value into
    /// an explicit row and "Default (CLI decides)" could not be expressed at all.</summary>
    [Fact]
    public async Task GetStoredAsync_returns_the_row_without_applying_the_fallback()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(new LlmSettings("haiku", LlmEffort.Low)));

        await svc.SaveAsync(tenant.Id, new LlmSettings("", LlmEffort.Max));

        var stored = await svc.GetStoredAsync(tenant.Id);
        Assert.Equal("", stored.Model);                 // NOT "haiku"
        Assert.Equal(LlmEffort.Max, stored.Effort);

        // ...while generation still runs on the resolved value.
        var resolved = await svc.GetAsync(tenant.Id);
        Assert.Equal("haiku", resolved.Model);
    }

    [Fact]
    public async Task GetStoredAsync_returns_Inherit_when_the_tenant_has_no_row()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(new LlmSettings("opus", LlmEffort.High)));

        Assert.Equal(LlmSettings.Inherit, await svc.GetStoredAsync(tenant.Id));
    }

    [Fact]
    public async Task GetStoredAsync_returns_an_explicit_choice_verbatim()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(new LlmSettings("haiku", LlmEffort.Low)));

        await svc.SaveAsync(tenant.Id, new LlmSettings("claude-opus-4-8", LlmEffort.XHigh));

        var stored = await svc.GetStoredAsync(tenant.Id);
        Assert.Equal("claude-opus-4-8", stored.Model);
        Assert.Equal(LlmEffort.XHigh, stored.Effort);
    }
}
