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

    // ---- #17: per-job overrides ----

    [Fact]
    public async Task A_job_override_wins_over_the_tenant_default_only_for_that_job()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(tenant.Id, new LlmSettings("haiku", LlmEffort.Low));                 // tenant default
        await svc.SaveAsync(tenant.Id, new LlmSettings("opus", LlmEffort.Max), LlmJobs.Research); // research override

        var research = await svc.GetAsync(tenant.Id, LlmJobs.Research);
        Assert.Equal("opus", research.Model);
        Assert.Equal(LlmEffort.Max, research.Effort);

        // A job with no override still resolves the tenant default.
        var subjects = await svc.GetAsync(tenant.Id, LlmJobs.SubjectIdeas);
        Assert.Equal("haiku", subjects.Model);
        Assert.Equal(LlmEffort.Low, subjects.Effort);

        // The no-job resolution (the pre-#17 behaviour) still reads the tenant default.
        var def = await svc.GetAsync(tenant.Id);
        Assert.Equal("haiku", def.Model);
    }

    [Fact]
    public async Task A_job_pinning_only_a_model_inherits_the_tenant_default_effort()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(tenant.Id, new LlmSettings("haiku", LlmEffort.Low));                       // default
        await svc.SaveAsync(tenant.Id, new LlmSettings("opus", LlmEffort.Default), LlmJobs.Research);  // model only

        var research = await svc.GetAsync(tenant.Id, LlmJobs.Research);
        Assert.Equal("opus", research.Model);          // the job override
        Assert.Equal(LlmEffort.Low, research.Effort);  // inherited from the tenant default
    }

    [Fact]
    public async Task A_job_pinning_only_an_effort_inherits_the_tenant_default_model()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(tenant.Id, new LlmSettings("haiku", LlmEffort.Low));               // default
        await svc.SaveAsync(tenant.Id, new LlmSettings("", LlmEffort.Max), LlmJobs.Research);  // effort only

        var research = await svc.GetAsync(tenant.Id, LlmJobs.Research);
        Assert.Equal("haiku", research.Model);         // inherited from the tenant default
        Assert.Equal(LlmEffort.Max, research.Effort);  // the job override
    }

    [Fact]
    public async Task A_job_override_falls_through_to_appsettings_when_there_is_no_tenant_default()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(new LlmSettings("haiku", LlmEffort.Low)));

        // Only a job override, no tenant-default row at all.
        await svc.SaveAsync(tenant.Id, new LlmSettings("opus", LlmEffort.Default), LlmJobs.Research);

        var research = await svc.GetAsync(tenant.Id, LlmJobs.Research);
        Assert.Equal("opus", research.Model);          // the job override
        Assert.Equal(LlmEffort.Low, research.Effort);  // appsettings, since neither job nor default pinned effort
    }

    [Fact]
    public async Task An_invalid_model_in_a_job_row_degrades_via_SafeModel_and_falls_back()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        test.Db.TenantLlmSettings.Add(new TenantLlmSetting { TenantId = tenant.Id, Job = null, Model = "haiku", Effort = "low" });
        test.Db.TenantLlmSettings.Add(new TenantLlmSetting
        {
            TenantId = tenant.Id, Job = LlmJobs.Research, Model = "opus --dangerously-skip-permissions", Effort = "high"
        });
        await test.Db.SaveChangesAsync();
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        var research = await svc.GetAsync(tenant.Id, LlmJobs.Research);
        Assert.Equal("haiku", research.Model);          // injected job model rejected → falls to tenant default
        Assert.Equal(LlmEffort.High, research.Effort);  // the job effort still applies
    }

    [Fact]
    public async Task Job_overrides_do_not_leak_across_tenants()
    {
        using var test = TestDb.Create();
        var a = await SeedTenant(test, "a");
        var b = await SeedTenant(test, "b");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(a.Id, new LlmSettings("opus", LlmEffort.Max), LlmJobs.Research);

        Assert.Equal("opus", (await svc.GetAsync(a.Id, LlmJobs.Research)).Model);
        Assert.Equal("", (await svc.GetAsync(b.Id, LlmJobs.Research)).Model);
    }

    [Fact]
    public async Task GetStoredAsync_distinguishes_a_job_override_from_an_inheriting_job()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(new LlmSettings("haiku", LlmEffort.Low)));

        await svc.SaveAsync(tenant.Id, new LlmSettings("sonnet", LlmEffort.High));                 // tenant default
        await svc.SaveAsync(tenant.Id, new LlmSettings("opus", LlmEffort.Max), LlmJobs.Research);  // research override

        Assert.Equal("opus", (await svc.GetStoredAsync(tenant.Id, LlmJobs.Research)).Model);       // its own row
        Assert.Equal(LlmSettings.Inherit, await svc.GetStoredAsync(tenant.Id, LlmJobs.SubjectIdeas)); // no row → Inherit
        Assert.Equal("sonnet", (await svc.GetStoredAsync(tenant.Id)).Model);                       // the default row itself
    }

    [Fact]
    public async Task Saving_a_fully_inherited_job_deletes_its_row_but_never_the_tenant_default()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(tenant.Id, new LlmSettings("opus", LlmEffort.Max), LlmJobs.Research);
        Assert.Equal(1, await test.Db.TenantLlmSettings.CountAsync(s => s.TenantId == tenant.Id && s.Job == LlmJobs.Research));

        // Clear both fields on the job → the override is deleted, not stored as an empty row.
        await svc.SaveAsync(tenant.Id, new LlmSettings("", LlmEffort.Default), LlmJobs.Research);
        Assert.Equal(0, await test.Db.TenantLlmSettings.CountAsync(s => s.TenantId == tenant.Id && s.Job == LlmJobs.Research));
        Assert.Equal(LlmSettings.Inherit, await svc.GetStoredAsync(tenant.Id, LlmJobs.Research));

        // A blank tenant default (job == null) is a legitimate "unset both flags" and is NOT deleted.
        await svc.SaveAsync(tenant.Id, new LlmSettings("", LlmEffort.Default));
        Assert.Equal(1, await test.Db.TenantLlmSettings.CountAsync(s => s.TenantId == tenant.Id && s.Job == null));
    }

    [Fact]
    public async Task The_database_forbids_two_tenant_default_rows_for_one_tenant()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        test.Db.TenantLlmSettings.Add(new TenantLlmSetting { TenantId = tenant.Id, Job = null, Model = "opus" });
        test.Db.TenantLlmSettings.Add(new TenantLlmSetting { TenantId = tenant.Id, Job = null, Model = "haiku" });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => test.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_database_forbids_two_rows_for_the_same_tenant_and_job()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        test.Db.TenantLlmSettings.Add(new TenantLlmSetting { TenantId = tenant.Id, Job = LlmJobs.Research, Model = "opus" });
        test.Db.TenantLlmSettings.Add(new TenantLlmSetting { TenantId = tenant.Id, Job = LlmJobs.Research, Model = "haiku" });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => test.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Different_jobs_and_the_default_coexist_for_one_tenant()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, Fallback(LlmSettings.Inherit));

        await svc.SaveAsync(tenant.Id, new LlmSettings("haiku", LlmEffort.Low));                      // default
        await svc.SaveAsync(tenant.Id, new LlmSettings("opus", LlmEffort.Max), LlmJobs.Research);     // research
        await svc.SaveAsync(tenant.Id, new LlmSettings("fable", LlmEffort.Low), LlmJobs.SubjectIdeas); // subject ideas

        Assert.Equal(3, await test.Db.TenantLlmSettings.CountAsync(s => s.TenantId == tenant.Id));
        Assert.Equal("opus", (await svc.GetAsync(tenant.Id, LlmJobs.Research)).Model);
        Assert.Equal("fable", (await svc.GetAsync(tenant.Id, LlmJobs.SubjectIdeas)).Model);
        Assert.Equal("haiku", (await svc.GetAsync(tenant.Id, LlmJobs.RecipeDraft)).Model); // no override → default
    }
}
