using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AthanTime.Net.Models;

namespace AthanTime.Tests;

/// <summary>Verifies the bundled JSON user-setting defaults.</summary>
public sealed class AppSettingsTests
{
    /// <summary>Ensures every displayed row, including sunrise, has independent reminder settings.</summary>
    [Fact]
    public void DefaultJson_ContainsNotificationSettingsForEveryRow()
    {
        using var stream = typeof(AppSettings).Assembly.GetManifestResourceStream(
            "AthanTime.Net.Data.default-settings.json");
        Assert.NotNull(stream);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
        var settings = JsonSerializer.Deserialize<AppSettings>(stream, options);

        Assert.NotNull(settings);
        Assert.Equal(Enum.GetValues<Prayer>().Length, settings.PrayerNotifications.Count);
        Assert.Contains(Prayer.Sunrise, settings.PrayerNotifications.Keys);
        Assert.Equal(15, new PrayerNotificationSettings().MinutesBefore);
        Assert.All(settings.PrayerNotifications, entry =>
        {
            Assert.Equal(15, entry.Value.MinutesBefore);
            Assert.Equal(entry.Key != Prayer.Sunrise, entry.Value.NotifyBefore);
        });
    }
}
