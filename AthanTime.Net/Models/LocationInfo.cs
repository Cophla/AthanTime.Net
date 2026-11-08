namespace AthanTime.Net.Models;

/// <summary>Represents one searchable offline populated place.</summary>
public sealed record LocationInfo(
    string Id,
    string City,
    string CountryCode,
    double Latitude,
    double Longitude,
    string TimeZoneId,
    IReadOnlyDictionary<string, string>? Names = null,
    IReadOnlyDictionary<string, string>? CountryNames = null,
    int Population = 0)
{
    /// <summary>Gets the localized city name using a case-insensitive language lookup.</summary>
    public string Name(string language) =>
        FindLocalizedName(Names, language) ?? City;

    /// <summary>Gets the localized country name using a case-insensitive language lookup.</summary>
    public string CountryName(string language) =>
        FindLocalizedName(CountryNames, language) ?? CountryCode;

    /// <summary>Finds one localized value without depending on the source dictionary comparer.</summary>
    private static string? FindLocalizedName(IReadOnlyDictionary<string, string>? names, string language) =>
        names?.FirstOrDefault(item => item.Key.Equals(language, StringComparison.OrdinalIgnoreCase)).Value;
}
