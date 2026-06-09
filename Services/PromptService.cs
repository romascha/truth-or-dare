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

    public PromptItem GetRandom(PromptType type, string? category)
    {
        var list = GetFiltered(type, category);
        if (list.Count == 0) throw new InvalidOperationException("No prompts configured.");
        return list[Random.Shared.Next(list.Count)];
    }

    public PromptItem? GetRandomExcluding(PromptType type, string? category, HashSet<string> usedTexts)
    {
        var list = GetFiltered(type, category)
            .Where(x => !usedTexts.Contains(x.Text))
            .ToList();
        if (list.Count == 0) return null;
        return list[Random.Shared.Next(list.Count)];
    }

    public int CountAvailable(string? category, HashSet<string> usedTexts)
    {
        var all = GetFilteredByCategory(category);
        return all.Count(x => !usedTexts.Contains(x.Text));
    }

    private List<PromptItem> GetFiltered(PromptType type, string? category)
    {
        var typeText = type.ToString();
        var query = _prompts.Value.Where(x => x.Type.Equals(typeText, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Any", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        var list = query.ToList();
        if (list.Count == 0)
            list = _prompts.Value.Where(x => x.Type.Equals(typeText, StringComparison.OrdinalIgnoreCase)).ToList();

        return list;
    }

    private List<PromptItem> GetFilteredByCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || category.Equals("Any", StringComparison.OrdinalIgnoreCase))
            return _prompts.Value;

        return _prompts.Value.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
