using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Web.Services;

namespace ContentAutomatorX.IntegrationTests;

public class FakeTenantIdStore : ITenantIdStore
{
    public Guid? Stored;
    public Task<Guid?> GetAsync() => Task.FromResult(Stored);
    public Task SetAsync(Guid id) { Stored = id; return Task.CompletedTask; }
}

public class TenantContextTests
{
    private static Tenant AddTenant(TestDb test, string name, bool active = true)
    {
        var tenant = new Tenant { Name = name, Slug = name.ToLowerInvariant(), IsActive = active };
        test.Db.Tenants.Add(tenant);
        test.Db.SaveChanges();
        return tenant;
    }

    [Fact]
    public async Task Initialize_restores_stored_tenant_and_raises_Changed()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Alpha");
        var beta = AddTenant(test, "Beta");
        var store = new FakeTenantIdStore { Stored = beta.Id };
        var ctx = new TenantContext(new TenantService(test.Db), store);
        var changed = 0;
        ctx.Changed += () => changed++;

        await ctx.InitializeAsync();

        Assert.True(ctx.Initialized);
        Assert.Equal(beta.Id, ctx.Active!.Id);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Initialize_with_stale_stored_id_falls_back_to_first_active_by_name_and_persists_it()
    {
        using var test = TestDb.Create();
        var alpha = AddTenant(test, "Alpha");
        AddTenant(test, "Beta");
        var store = new FakeTenantIdStore { Stored = Guid.NewGuid() };
        var ctx = new TenantContext(new TenantService(test.Db), store);

        await ctx.InitializeAsync();

        Assert.Equal(alpha.Id, ctx.Active!.Id);
        Assert.Equal(alpha.Id, store.Stored);
    }

    [Fact]
    public async Task Initialize_with_no_active_tenants_leaves_Active_null()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Dormant", active: false);
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore());

        await ctx.InitializeAsync();

        Assert.True(ctx.Initialized);
        Assert.Null(ctx.Active);
        Assert.Empty(ctx.ActiveTenants);
    }

    [Fact]
    public async Task Inactive_tenants_are_excluded_from_ActiveTenants()
    {
        using var test = TestDb.Create();
        var alpha = AddTenant(test, "Alpha");
        AddTenant(test, "Dormant", active: false);
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore());

        await ctx.InitializeAsync();

        Assert.Equal([alpha.Id], ctx.ActiveTenants.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Initialize_pre_highlights_a_tenant_but_does_not_confirm_selection()
    {
        using var test = TestDb.Create();
        var beta = AddTenant(test, "Beta");
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore { Stored = beta.Id });

        await ctx.InitializeAsync();

        Assert.Equal(beta.Id, ctx.Active!.Id);   // pre-highlighted for the picker
        Assert.False(ctx.SelectionConfirmed);      // ...but the user hasn't entered yet
    }

    [Fact]
    public async Task Enter_confirms_selection_sets_active_persists_and_raises_Changed()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Alpha");
        var beta = AddTenant(test, "Beta");
        var store = new FakeTenantIdStore();
        var ctx = new TenantContext(new TenantService(test.Db), store);
        await ctx.InitializeAsync();
        var changed = 0;
        ctx.Changed += () => changed++;

        await ctx.EnterAsync(beta.Id);

        Assert.True(ctx.SelectionConfirmed);
        Assert.Equal(beta.Id, ctx.Active!.Id);
        Assert.Equal(beta.Id, store.Stored);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Enter_confirms_even_when_tenant_is_already_the_pre_highlighted_active()
    {
        // The SwitchAsync no-op trap: clicking the remembered tenant must still enter.
        using var test = TestDb.Create();
        var beta = AddTenant(test, "Beta");
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore { Stored = beta.Id });
        await ctx.InitializeAsync();
        Assert.Equal(beta.Id, ctx.Active!.Id);   // already active from restore
        var changed = 0;
        ctx.Changed += () => changed++;

        await ctx.EnterAsync(beta.Id);

        Assert.True(ctx.SelectionConfirmed);
        Assert.Equal(1, changed);   // Changed still raised, unlike SwitchAsync
    }

    [Fact]
    public async Task Enter_with_unknown_id_is_ignored_and_leaves_picker_unconfirmed()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Alpha");
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore());
        await ctx.InitializeAsync();
        var changed = 0;
        ctx.Changed += () => changed++;

        await ctx.EnterAsync(Guid.NewGuid());

        Assert.False(ctx.SelectionConfirmed);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Switch_sets_active_persists_and_raises_Changed_but_ignores_unknown_ids()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Alpha");
        var beta = AddTenant(test, "Beta");
        var store = new FakeTenantIdStore();
        var ctx = new TenantContext(new TenantService(test.Db), store);
        await ctx.InitializeAsync();
        var changed = 0;
        ctx.Changed += () => changed++;

        await ctx.SwitchAsync(beta.Id);
        Assert.Equal(beta.Id, ctx.Active!.Id);
        Assert.Equal(beta.Id, store.Stored);
        Assert.Equal(1, changed);

        await ctx.SwitchAsync(Guid.NewGuid());   // unknown id → no-op
        Assert.Equal(beta.Id, ctx.Active!.Id);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Refresh_after_deactivating_active_tenant_falls_back_and_raises_Changed()
    {
        using var test = TestDb.Create();
        var alpha = AddTenant(test, "Alpha");
        var beta = AddTenant(test, "Beta");
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore());
        await ctx.InitializeAsync();
        await ctx.SwitchAsync(beta.Id);
        beta.IsActive = false;
        test.Db.SaveChanges();
        var changed = 0;
        ctx.Changed += () => changed++;

        await ctx.RefreshAsync();

        Assert.Equal(alpha.Id, ctx.Active!.Id);
        Assert.Equal(1, changed);
        Assert.DoesNotContain(ctx.ActiveTenants, t => t.Id == beta.Id);
    }

    [Fact]
    public async Task Refresh_keeps_current_active_tenant_when_still_valid()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Alpha");
        var beta = AddTenant(test, "Beta");
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore());
        await ctx.InitializeAsync();
        await ctx.SwitchAsync(beta.Id);

        await ctx.RefreshAsync();

        Assert.Equal(beta.Id, ctx.Active!.Id);
    }

    [Fact]
    public async Task Refresh_with_preferred_id_re_lists_and_selects_in_one_Changed()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Alpha");
        var beta = AddTenant(test, "Beta");
        var store = new FakeTenantIdStore();
        var ctx = new TenantContext(new TenantService(test.Db), store);
        await ctx.InitializeAsync();
        var changed = 0;
        ctx.Changed += () => changed++;

        await ctx.RefreshAsync(beta.Id);

        Assert.Equal(beta.Id, ctx.Active!.Id);
        Assert.Equal(beta.Id, store.Stored);
        Assert.Equal(1, changed);
    }
}
