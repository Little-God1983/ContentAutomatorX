using System.Net;
using System.Text;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Platforms;

namespace ContentAutomatorX.UnitTests;

public class MailerLiteClientTests
{
    private static readonly MailerLiteDraft Draft = new(
        Name: "AI Weekly #1", Subject: "subj", PreviewText: "pv",
        FromName: "AIVisions", FromEmail: "news@example.com", GroupId: "g1", Html: "<html></html>");

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ListGroups_sends_bearer_and_parses_groups()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":[{"id":"g1","name":"Main list"}]}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        var groups = await client.ListGroupsAsync("KEY");

        var g = Assert.Single(groups);
        Assert.Equal(("g1", "Main list"), (g.Id, g.Name));
        var req = Assert.Single(handler.Requests);
        Assert.Equal("Bearer KEY", req.Headers.Authorization!.ToString());
        Assert.EndsWith("/groups", req.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PushDraft_creates_a_campaign_and_returns_its_id()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42","status":"draft"}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        var id = await client.PushDraftAsync("KEY", Draft, existingCampaignId: null);

        Assert.Equal("c42", id);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/campaigns", req.RequestUri!.AbsolutePath);
        var body = await req.Content!.ReadAsStringAsync();
        Assert.Contains("\"subject\":\"subj\"", body);
        Assert.Contains("\"groups\":[\"g1\"]", body);
        Assert.Contains("\"type\":\"regular\"", body);
    }

    [Fact]
    public async Task PushDraft_with_existing_id_updates_via_put()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42","status":"draft"}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        var id = await client.PushDraftAsync("KEY", Draft, existingCampaignId: "c42");

        Assert.Equal("c42", id);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.EndsWith("/campaigns/c42", req.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetStatus_parses_sent_status_with_stats()
    {
        var handler = new StubHttpHandler(_ => Json(
            """{"data":{"id":"c42","status":"sent","stats":{"sent":1204,"opens_count":577,"clicks_count":89}}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        var status = await client.GetStatusAsync("KEY", "c42");

        Assert.Equal("sent", status.Status);
        Assert.Equal(1204, status.Sent);
        Assert.Equal(577, status.OpensCount);
        Assert.Equal(89, status.ClicksCount);
    }

    [Fact]
    public async Task GetStatus_without_stats_yields_nulls()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42","status":"draft"}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));
        var status = await client.GetStatusAsync("KEY", "c42");
        Assert.Equal("draft", status.Status);
        Assert.Null(status.Sent);
    }

    [Fact]
    public async Task Unauthorized_throws_with_actionable_message()
    {
        var handler = new StubHttpHandler(_ => Json("""{"message":"Unauthenticated."}""", HttpStatusCode.Unauthorized));
        var client = new MailerLiteClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListGroupsAsync("BAD"));
        Assert.Contains("401", ex.Message);
        Assert.False(await client.TestAsync("BAD")); // TestAsync swallows into false
    }

    [Fact]
    public async Task GetStatus_sends_get_with_bearer_to_the_campaign_path()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42","status":"draft"}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        await client.GetStatusAsync("KEY", "c42");

        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/campaigns/c42", req.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer KEY", req.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task GetStatus_without_status_property_reports_unknown()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42"}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        var status = await client.GetStatusAsync("KEY", "c42");

        Assert.Equal("unknown", status.Status);
    }

    [Fact]
    public async Task GetStatus_with_partial_stats_fills_what_it_can()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42","status":"sent","stats":{"sent":10}}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        var status = await client.GetStatusAsync("KEY", "c42");

        Assert.Equal(10, status.Sent);
        Assert.Null(status.OpensCount);
        Assert.Null(status.ClicksCount);
    }

    [Fact]
    public async Task GetStatus_error_response_throws_with_status_code()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            { Content = new StringContent("""{"message":"nope"}""") });
        var client = new MailerLiteClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetStatusAsync("KEY", "c42"));

        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task PushDraft_create_sends_every_campaign_field()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c1"}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));
        var draft = new MailerLiteDraft(
            Name: "Weekly #1", Subject: "Subject A", PreviewText: "Preview B",
            FromName: "Chris", FromEmail: "chris@ex.com", GroupId: "g1", Html: "<p>x</p>");

        var id = await client.PushDraftAsync("KEY", draft, existingCampaignId: null);

        Assert.Equal("c1", id);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/campaigns", req.RequestUri!.AbsolutePath);
        using var body = System.Text.Json.JsonDocument.Parse(await req.Content!.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("Weekly #1", root.GetProperty("name").GetString());
        Assert.Equal("regular", root.GetProperty("type").GetString());
        Assert.Equal("g1", root.GetProperty("groups")[0].GetString());
        var email = root.GetProperty("emails")[0];
        Assert.Equal("Subject A", email.GetProperty("subject").GetString());
        Assert.Equal("Preview B", email.GetProperty("preview_text").GetString());
        Assert.Equal("Chris", email.GetProperty("from_name").GetString());
        Assert.Equal("chris@ex.com", email.GetProperty("from").GetString());
        Assert.Equal("<p>x</p>", email.GetProperty("content").GetString());
    }
}
