namespace ContentAutomatorX.Domain.Models;

public record MailerLiteGroup(string Id, string Name);
public record MailerLiteCampaignStatus(string Status, int? Sent, int? OpensCount, int? ClicksCount);
public record MailerLiteDraft(string Name, string Subject, string? PreviewText,
    string FromName, string FromEmail, string GroupId, string Html);
