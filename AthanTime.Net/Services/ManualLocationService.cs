using AthanTime.Net.Models;

namespace AthanTime.Net.Services;

/// <summary>Validates coordinates and canonicalizes IANA zones without guessing a zone from longitude.</summary>
public sealed class ManualLocationService(TimeZoneService timeZones)
{
    /// <summary>Bundled IANA identifiers, identical on every platform.</summary>
    public IReadOnlyList<string> TimeZones => timeZones.Ids;

    /// <summary>Returns a validated location, or a localization key describing invalid input.</summary>
    public bool TryCreate(string name, double latitude, double longitude, string zone, string country,
        out LocationInfo? location, out string error)
    {
        location = null;
        error = "InvalidCoordinates";
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100
            || !double.IsFinite(latitude) || latitude is < -90 or > 90
            || !double.IsFinite(longitude) || longitude is < -180 or > 180) return false;
        error = "InvalidTimeZone";
        var canonicalZone = TimeZones.FirstOrDefault(id => id.Equals(zone?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (canonicalZone is null) return false;
        country = (country ?? "").Trim().ToUpperInvariant();
        error = "InvalidCountry";
        if (country.Length != 0 && (country.Length != 2 || country.Any(c => c is < 'A' or > 'Z'))) return false;
        location = new LocationInfo("manual-" + Guid.NewGuid().ToString("N"), name.Trim(), country,
            latitude, longitude, canonicalZone);
        error = "";
        return true;
    }
}
