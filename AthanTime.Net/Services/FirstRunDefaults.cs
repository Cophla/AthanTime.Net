using System.Globalization;
using AthanTime.Net.Models;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.TimeZones;

namespace AthanTime.Net.Services;

/// <summary>Uses OS culture and TZDB mapping for first-run hints, never exact location detection.</summary>
public sealed class FirstRunDefaults(ILocationRepository locations, IDateTimeZoneProvider timeZones, ILogger<FirstRunDefaults> logger)
{
    /// <summary>Reads the current OS user's regional hints only for an absent profile.</summary>
    public void Apply(AppSettings settings)
    {
        string zone = "UTC", country = "";
        try { zone = timeZones.GetSystemDefault().Id; }
        catch (DateTimeZoneNotFoundException exception) { logger.LogWarning(exception, "OS time zone could not be mapped to TZDB"); }
        try { country = RegionInfo.CurrentRegion.TwoLetterISORegionName; }
        catch (ArgumentException exception) { logger.LogWarning(exception, "OS region is unavailable"); }
        Apply(settings, CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture, country, zone);
    }

    /// <summary>Applies injectable culture/region/zone hints; saved settings never pass through this path.</summary>
    public void Apply(AppSettings settings, CultureInfo uiCulture, CultureInfo formatCulture, string country, string zone)
    {
        var language = uiCulture.TwoLetterISOLanguageName;
        settings.Language = LocalizationService.Languages.FirstOrDefault(item =>
            item.Code.Equals(language, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
        settings.Use24HourClock = formatCulture.DateTimeFormat.ShortTimePattern.Contains('H');
        settings.LocationNeedsConfirmation = true;
        var aliases = TzdbDateTimeZoneSource.Default.CanonicalIdMap;
        if (!aliases.TryGetValue(zone, out var canonical)) return;
        var zones = aliases.Where(item => item.Value.Equals(canonical, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Key).ToArray();
        var suggestion = locations.Suggest(zones, country);
        if (suggestion is not null) settings.LocationId = suggestion.Id;
        logger.LogInformation("First-run regional defaults applied; city requires confirmation, suggestion available: {Suggested}", suggestion is not null);
    }
}
