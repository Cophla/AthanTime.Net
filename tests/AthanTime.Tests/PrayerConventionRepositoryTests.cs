using AthanTime.Net.Models;
using AthanTime.Net.Services;

namespace AthanTime.Tests;

/// <summary>Verifies JSON-backed automatic convention resolution.</summary>
public sealed class PrayerConventionRepositoryTests(IPrayerConventionRepository conventions)
{
    private readonly IPrayerConventionRepository _conventions = conventions;

    /// <summary>Checks country matching without case sensitivity.</summary>
    [Fact]
    public void Resolve_IsCaseInsensitiveForCountryCodes()
    {
        var location = new LocationInfo("test", "Makkah", "sa", 21.3891, 39.8579, "Asia/Riyadh");

        Assert.Equal("Makkah", _conventions.Resolve(location).MethodId, ignoreCase: true);
    }

    /// <summary>Checks that every unlisted country and its cities receive the documented fallback.</summary>
    [Fact]
    public void Resolve_UsesGlobalFallbackForEveryUnlistedCountry()
    {
        var location = new LocationInfo("test", "Test", "ZZ", 0, 0, "Etc/UTC");

        Assert.Equal("MWL", _conventions.Resolve(location).MethodId, ignoreCase: true);
    }

    /// <summary>Checks the Egyptian convention shown by the supplied Sidon reference.</summary>
    [Fact]
    public void Resolve_Sidon_UsesSuppliedReferenceConvention()
    {
        var location = new LocationInfo("sidon", "Sidon", "lb", 33.5631, 35.3689, "Asia/Beirut");

        Assert.Equal("Egypt", _conventions.Resolve(location).MethodId, ignoreCase: true);
    }

    /// <summary>Ensures the removed Tehran preset is not selected automatically.</summary>
    [Fact]
    public void Resolve_Iran_UsesSunniFallback()
    {
        var location = new LocationInfo("tehran", "Tehran", "IR", 35.6892, 51.389, "Asia/Tehran");

        Assert.Equal("MWL", _conventions.Resolve(location).MethodId, ignoreCase: true);
    }
}
