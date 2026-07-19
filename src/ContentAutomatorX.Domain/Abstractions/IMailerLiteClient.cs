using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface IMailerLiteClient
{
    Task<bool> TestAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<MailerLiteGroup>> ListGroupsAsync(string apiKey, CancellationToken ct = default);
    /// <returns>Campaign id (creates when existingCampaignId is null, else updates).</returns>
    Task<string> PushDraftAsync(string apiKey, MailerLiteDraft draft, string? existingCampaignId, CancellationToken ct = default);
    Task<MailerLiteCampaignStatus> GetStatusAsync(string apiKey, string campaignId, CancellationToken ct = default);
}
