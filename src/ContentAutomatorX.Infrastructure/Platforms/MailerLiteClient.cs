using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Platforms;

/// <summary>MailerLite "new" API (https://connect.mailerlite.com/api). Creates/updates DRAFT
/// campaigns only — sending stays a human act in MailerLite (spec: Send is never automated).</summary>
public class MailerLiteClient(HttpClient http) : IMailerLiteClient
{
    public const string BaseUrl = "https://connect.mailerlite.com/api";

    public async Task<bool> TestAsync(string apiKey, CancellationToken ct = default)
    {
        try { await ListGroupsAsync(apiKey, ct); return true; }
        catch { return false; }
    }

    public async Task<IReadOnlyList<MailerLiteGroup>> ListGroupsAsync(string apiKey, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, "/groups", apiKey, payload: null, ct);
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(g => new MailerLiteGroup(
                g.GetProperty("id").ToString(),
                g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""))
            .ToList();
    }

    public async Task<string> PushDraftAsync(string apiKey, MailerLiteDraft draft,
        string? existingCampaignId, CancellationToken ct = default)
    {
        var payload = new
        {
            name = draft.Name,
            type = "regular",
            groups = new[] { draft.GroupId },
            emails = new[]
            {
                new
                {
                    subject = draft.Subject,
                    preview_text = draft.PreviewText,
                    from_name = draft.FromName,
                    from = draft.FromEmail,
                    content = draft.Html
                }
            }
        };
        var (method, path) = existingCampaignId is null
            ? (HttpMethod.Post, "/campaigns")
            : (HttpMethod.Put, $"/campaigns/{existingCampaignId}");
        using var doc = await SendAsync(method, path, apiKey, payload, ct);
        return doc.RootElement.GetProperty("data").GetProperty("id").ToString();
    }

    public async Task<MailerLiteCampaignStatus> GetStatusAsync(string apiKey, string campaignId,
        CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/campaigns/{campaignId}", apiKey, payload: null, ct);
        var data = doc.RootElement.GetProperty("data");
        var status = data.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";
        int? sent = null, opens = null, clicks = null;
        if (data.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
        {
            sent = TryInt(stats, "sent");
            opens = TryInt(stats, "opens_count");
            clicks = TryInt(stats, "clicks_count");
        }
        return new MailerLiteCampaignStatus(status, sent, opens, clicks);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string apiKey,
        object? payload, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (payload is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"MailerLite {method} {path} failed: {(int)response.StatusCode} {Truncate(body)}");
        return JsonDocument.Parse(body);
    }

    private static int? TryInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "...";
}
