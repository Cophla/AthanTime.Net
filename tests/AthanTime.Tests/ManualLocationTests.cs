using AthanTime.Net.Services;

namespace AthanTime.Tests;

/// <summary>Validates offline coordinate entry independently of UI formatting.</summary>
public sealed class ManualLocationTests(ManualLocationService locations)
{
    [Theory]
    [InlineData(33.5631, 35.3689, "asia/beirut", "lb", "Asia/Beirut")]
    [InlineData(-33.8688, 151.2093, "AUSTRALIA/SYDNEY", "AU", "Australia/Sydney")]
    [InlineData(0, 0, "Etc/UTC", "", "Etc/UTC")]
    [InlineData(90, -180, "Etc/UTC", "", "Etc/UTC")]
    public void ValidCoordinates_PreservePrecisionAndCanonicalZone(double lat, double lon, string zone, string country, string expected)
    {
        Assert.True(locations.TryCreate("Custom", lat, lon, zone, country, out var place, out var error), error);
        Assert.NotNull(place);
        Assert.Equal(lat, place.Latitude);
        Assert.Equal(lon, place.Longitude);
        Assert.Equal(expected, place.TimeZoneId);
    }

    [Theory]
    [InlineData("", 0, 0, "Etc/UTC", "")]
    [InlineData("City", 91, 0, "Etc/UTC", "")]
    [InlineData("City", 0, -181, "Etc/UTC", "")]
    [InlineData("City", double.NaN, 0, "Etc/UTC", "")]
    [InlineData("City", 0, double.PositiveInfinity, "Etc/UTC", "")]
    [InlineData("City", 0, 0, "GMT+3", "")]
    [InlineData("City", 0, 0, "Etc/UTC", "Lebanon")]
    public void InvalidInput_IsRejected(string name, double lat, double lon, string zone, string country) =>
        Assert.False(locations.TryCreate(name, lat, lon, zone, country, out _, out _));

    [Fact]
    public void BeirutZone_UsesDateSpecificDaylightSaving()
    {
        Assert.True(locations.TryCreate("Sidon", 33.5631, 35.3689, "ASIA/BEIRUT", "LB", out var place, out _));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(place!.TimeZoneId);
        Assert.Equal(TimeSpan.FromHours(2), zone.GetUtcOffset(new DateTime(2026, 1, 15)));
        Assert.Equal(TimeSpan.FromHours(3), zone.GetUtcOffset(new DateTime(2026, 9, 20)));
    }
}
