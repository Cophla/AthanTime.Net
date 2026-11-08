using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.Services;

/// <summary>Reads the bundled, sourced morning/evening/general Adhkar and their eight meanings.</summary>
public sealed class DefaultAdhkarRepository
{
    private readonly List<Entry> _entries;

    /// <summary>Loads the offline JSON once; no network or user-profile access is needed.</summary>
    public DefaultAdhkarRepository(ILogger<DefaultAdhkarRepository> logger)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AthanTime.Net.Data.default-adhkar.json")
            ?? throw new InvalidOperationException("Bundled Adhkar JSON is missing.");
        _entries = JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Entries
            ?? throw new InvalidOperationException("Bundled Adhkar JSON is invalid.");
        logger.LogInformation("Loaded {Count} bundled Adhkar texts", _entries.Count);
    }

    /// <summary>Returns Arabic recitation plus meaning in the selected language for the local hour.</summary>
    public IReadOnlyList<string> GetTexts(string language, int localHour) => _entries
        .Where(entry => entry.Period.Equals("any", StringComparison.OrdinalIgnoreCase)
            || (localHour is >= 4 and < 12 && entry.Period.Equals("morning", StringComparison.OrdinalIgnoreCase))
            || (localHour is >= 16 and < 24 && entry.Period.Equals("evening", StringComparison.OrdinalIgnoreCase)))
        .Select(entry => entry.Arabic + "\n" + (entry.Meanings.FirstOrDefault(item =>
            item.Key.Equals(language, StringComparison.OrdinalIgnoreCase)).Value
            ?? entry.Meanings.GetValueOrDefault("en") ?? ""))
        .ToArray();

    private sealed class Catalog { public List<Entry> Entries { get; set; } = []; }
    private sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Period { get; set; } = "any";
        public string Arabic { get; set; } = "";
        public Dictionary<string, string> Meanings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
