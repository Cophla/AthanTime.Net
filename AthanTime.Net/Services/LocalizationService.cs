using System.Text.Json;

namespace AthanTime.Net.Services;

/// <summary>Describes a selectable UI language and its reading direction.</summary>
public sealed record LanguageOption(string Code, string NativeName, bool IsRightToLeft);

/// <summary>Returns localized UI text and supported language metadata.</summary>
public interface ILocalizationService
{
    /// <summary>Gets localized text with case-insensitive language and key lookup.</summary>
    string Get(string language, string key);
}

/// <summary>Loads all captions from the embedded JSON; unknown languages use English and unknown keys remain visible.</summary>
public sealed class LocalizationService : ILocalizationService
{
    private static readonly Dictionary<string, Dictionary<string, string>> _text = LoadText();

    /// <summary>Gets the languages bundled with the application.</summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("en", "English", false), new("ar", "العربية", true),
        new("fr", "Français", false), new("es", "Español", false),
        new("tr", "Türkçe", false), new("de", "Deutsch", false),
        new("id", "Bahasa Indonesia", false), new("ur", "اردو", true)
    ];

    /// <inheritdoc />
    public string Get(string language, string key)
    {
        if (_text.TryGetValue(language, out var captions) && captions.TryGetValue(key, out var value)) return value;
        return _text["en"].GetValueOrDefault(key, key);
    }

    private static Dictionary<string, Dictionary<string, string>> LoadText()
    {
        using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream("AthanTime.Net.Data.ui-text.json")
            ?? throw new InvalidOperationException("Bundled UI text JSON is missing.");
        var text = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
            ?? throw new InvalidDataException("Bundled UI text JSON is empty.");
        // Deserialized dictionaries use the default comparer; normalize both language and caption identities.
        return text.ToDictionary(pair => pair.Key,
            pair => new Dictionary<string, string>(pair.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }
}
