using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AthanTime.Net.Models;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.Services;

/// <summary>Resolves bundled regional calculation conventions for every offline place.</summary>
public interface IPrayerConventionRepository
{
    /// <summary>Returns a city rule, country rule, or the documented global fallback.</summary>
    PrayerConvention Resolve(LocationInfo location);
}

/// <inheritdoc />
public sealed class PrayerConventionRepository : IPrayerConventionRepository
{
    private const string ResourceName = "AthanTime.Net.Data.prayer-conventions.json";
    private readonly PrayerConventionData _data;
    private readonly ILogger<PrayerConventionRepository> _logger;

    /// <summary>Loads the embedded convention data once.</summary>
    public PrayerConventionRepository(ILogger<PrayerConventionRepository> logger)
    {
        _logger = logger;
        _data = Load();
    }

    /// <inheritdoc />
    public PrayerConvention Resolve(LocationInfo location)
    {
        var rule = _data.Rules.FirstOrDefault(item =>
                item.LocationIds.Contains(location.Id, StringComparer.OrdinalIgnoreCase))
            ?? _data.Rules.FirstOrDefault(item =>
                item.CountryCodes.Contains(location.CountryCode, StringComparer.OrdinalIgnoreCase))
            ?? _data.Default;

        _logger.LogDebug("Resolved convention {ConventionId} for {LocationId}", rule.Id, location.Id);
        return new PrayerConvention(rule.Id, CalculationMethod.Find(rule.MethodId).Id,
            rule.AsrJuristic, rule.HighLatitudeRule, rule.Reference);
    }

    private PrayerConventionData Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var data = JsonSerializer.Deserialize<PrayerConventionData>(stream, options)
            ?? throw new InvalidDataException("Prayer convention JSON is empty.");
        _logger.LogInformation("Loaded {Count} prayer convention rules dated {Updated}", data.Rules.Length, data.Updated);
        return data;
    }
}
