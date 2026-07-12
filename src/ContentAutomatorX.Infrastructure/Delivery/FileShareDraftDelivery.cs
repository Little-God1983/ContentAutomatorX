using System.Text;
using System.Text.Json;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Delivery;

public class FileShareDraftDelivery : IDraftDelivery
{
    public async Task<string> DeliverAsync(Tenant tenant, RecipeOutput output, Draft draft, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant.OutputFolderPath))
            throw new InvalidOperationException($"Tenant '{tenant.Slug}' has no OutputFolderPath configured");

        var folder = string.IsNullOrWhiteSpace(output.Subfolder)
            ? tenant.OutputFolderPath
            : Path.Combine(tenant.OutputFolderPath, output.Subfolder);
        Directory.CreateDirectory(folder);

        var pattern = string.IsNullOrWhiteSpace(output.FilenamePattern) ? "{date}-{kind}-{slug}.md" : output.FilenamePattern;
        var baseName = pattern
            .Replace("{date}", draft.CreatedAt.ToString("yyyy-MM-dd"))
            .Replace("{kind}", draft.Kind.ToLowerInvariant())
            .Replace("{slug}", Slugify(draft.Title));

        var path = Path.Combine(folder, baseName);
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder,
                Path.GetFileNameWithoutExtension(baseName) + $"-{n}" + Path.GetExtension(baseName));

        var content = BuildContent(tenant, draft);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, content, new UTF8Encoding(false), ct);
        File.Move(tmp, path);
        return Path.GetFullPath(path);
    }

    private static string BuildContent(Tenant tenant, Draft draft)
    {
        var itemIds = JsonSerializer.Deserialize<string[]>(draft.SourceItemIdsJson) ?? [];
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"tenant: {tenant.Slug}");
        sb.AppendLine($"recipe: {draft.RecipeId}");
        sb.AppendLine($"kind: {draft.Kind}");
        sb.AppendLine($"created: {draft.CreatedAt:O}");
        sb.AppendLine($"model: {draft.ModelUsed}");
        sb.AppendLine($"source_items: [{string.Join(", ", itemIds)}]");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(draft.Body);
        return sb.ToString();
    }

    private static string Slugify(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "draft" : slug[..Math.Min(slug.Length, 60)].Trim('-');
    }
}
