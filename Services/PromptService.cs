using System.Text.Json;
using TruthOrDare.Api.Models;

namespace TruthOrDare.Api.Services;

public sealed class PromptService(IWebHostEnvironment env)
{
    private readonly Lazy<List<PromptItem>> _prompts = new(() =>
    {
        var path = Path.Combine(env.ContentRootPath, "prompts.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<PromptItem>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    });

    public PromptItem? GetRandomExcluding(PromptType type, string? category, string? playerGender, HashSet<string> usedTexts)
    {
        var list = GetFiltered(type, category, playerGender)
            .Where(x => !usedTexts.Contains(x.Text))
            .ToList();
        if (list.Count == 0) return null;
        return list[Random.Shared.Next(list.Count)];
    }

    public int CountAvailable(string? category, string? playerGender, HashSet<string> usedTexts)
    {
        var all = GetFilteredByCategory(category, playerGender);
        return all.Count(x => !usedTexts.Contains(x.Text));
    }

    private List<PromptItem> GetFiltered(PromptType type, string? category, string? playerGender)
    {
        var typeText = type.ToString();
        var query = _prompts.Value
            .Where(x => x.Type.Equals(typeText, StringComparison.OrdinalIgnoreCase))
            .Where(x => GenderMatches(x.Gender, playerGender));

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Any", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        return query.ToList();
    }

    private List<PromptItem> GetFilteredByCategory(string? category, string? playerGender)
    {
        var query = _prompts.Value.Where(x => GenderMatches(x.Gender, playerGender));

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Any", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        return query.ToList();
    }

    private static bool GenderMatches(string? promptGender, string? playerGender)
    {
        // null or empty or "Any" means the prompt is for everyone
        if (string.IsNullOrWhiteSpace(promptGender) || promptGender.Equals("Any", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(playerGender))
            return true;
        return promptGender.Equals(playerGender, StringComparison.OrdinalIgnoreCase);
    }
}
