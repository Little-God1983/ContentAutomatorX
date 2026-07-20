using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class LlmSettingsServiceTests
{
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
        var svc = new LlmSettingsService(test.Db, new LlmSettings("haiku", LlmEffort.Low));

        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("haiku", settings.Model);
        Assert.Equal(LlmEffort.Low, settings.Effort);
    }

    [Fact]
    public async Task Unconfigured_tenant_with_blank_appsettings_inherits()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

        Assert.Equal(LlmSettings.Inherit, await svc.GetAsync(tenant.Id));
    }

    [Fact]
    public async Task Saved_values_round_trip()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

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
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

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
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

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
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveAsync(tenant.Id, new LlmSettings("opus --dangerously-skip-permissions", LlmEffort.Default)));

        Assert.Equal(0, await test.Db.TenantLlmSettings.CountAsync());
    }

    [Fact]
    public async Task Save_accepts_a_blank_model_as_meaning_unset()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

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
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("opus", settings.Model);
        Assert.Equal(LlmEffort.Default, settings.Effort);
    }

    [Fact]
    public async Task A_row_with_a_blank_model_still_falls_back_for_the_model_only()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, new LlmSettings("haiku", LlmEffort.Low));

        await svc.SaveAsync(tenant.Id, new LlmSettings("", LlmEffort.Max));
        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("haiku", settings.Model);      // blank model -> appsettings
        Assert.Equal(LlmEffort.Max, settings.Effort); // explicit effort wins
    }
}
