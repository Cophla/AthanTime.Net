using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AthanTime.Net.Models;
using AthanTime.Net.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AthanTime.Tests;

/// <summary>Compares SQLite results with the previous catalog's matching and ranking contract.</summary>
public sealed class LocationSearchParityTests(ILocationRepository locations)
{
    private static readonly Lazy<LocationInfo[]> _reference = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "ReferenceData", "locations.json.br"));
        using var stream = new BrotliStream(file, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(stream);
        var countries = json.RootElement.GetProperty("countries");
        return json.RootElement.GetProperty("locations").EnumerateArray().Select(x =>
        {
            var code = x.GetProperty("countryCode").GetString()!;
            return new LocationInfo(x.GetProperty("id").GetString()!, x.GetProperty("city").GetString()!,
                code, x.GetProperty("latitude").GetDouble(), x.GetProperty("longitude").GetDouble(),
                x.GetProperty("timeZoneId").GetString()!,
                x.TryGetProperty("names", out var names) ? names.Deserialize<Dictionary<string, string>>() : null,
                countries.GetProperty(code).Deserialize<Dictionary<string, string>>(), x.GetProperty("population").GetInt32());
        }).ToArray();
    });

    [Theory]
    [InlineData("be", "en")]
    [InlineData("BEIRUT", "EN")]
    [InlineData("بيروت", "ar")]
    [InlineData("Turquie", "fr")]
    [InlineData("Espa\u00f1a", "es")]
    [InlineData("\u0130stanbul", "tr")]
    [InlineData("Deutschland", "de")]
    [InlineData("Indonesia", "id")]
    [InlineData("پاکستان", "ur")]
    [InlineData("S\u00e3o", "en")]
    [InlineData("an", "ar")]
    [InlineData("*", "en")]
    [InlineData("??", "en")]
    [InlineData("[a", "en")]
    [InlineData("a_b", "en")]
    [InlineData("a%b", "en")]
    [InlineData("London", "unknown")]
    public void Search_PreservesPreviousResults(string query, string language)
    {
        var needle = Normalize(query);
        var expected = needle.Length < 2 ? [] : _reference.Value
            .Select(x => (Location: x, Rank: Rank(x, needle, language)))
            .Where(x => x.Rank < 4).OrderBy(x => x.Rank).ThenByDescending(x => x.Location.Population)
            .Take(30).Select(x => x.Location.Id).ToArray();
        Assert.Equal(expected, locations.Search(query, language).Select(x => x.Id).ToArray());
    }

    /// <summary>Prevents empty parity results from disguising broken Unicode test inputs.</summary>
    [Theory]
    [InlineData("Beirut", "en", "LB")]
    [InlineData("\u0628\u064a\u0631\u0648\u062a", "ar", "LB")]
    [InlineData("Turquie", "fr", "TR")]
    [InlineData("Espa\u00f1a", "es", "ES")]
    [InlineData("\u0130stanbul", "tr", "TR")]
    [InlineData("Deutschland", "de", "DE")]
    [InlineData("Indonesia", "id", "ID")]
    [InlineData("\u067e\u0627\u06a9\u0633\u062a\u0627\u0646", "ur", "PK")]
    [InlineData("S\u00e3o", "en", "BR")]
    public void Search_FindsKnownPlacesInEveryLanguage(string query, string language, string country) =>
        Assert.Contains(locations.Search(query, language), place => place.CountryCode.Equals(country, StringComparison.OrdinalIgnoreCase));

    /// <summary>Checks identifiers, coordinates, zone IDs and translations survive catalog conversion.</summary>
    [Theory]
    [InlineData("geonames-276781")]
    [InlineData("geonames-2643743")]
    [InlineData("geonames-745044")]
    public void Get_PreservesArchivedLocationFields(string id)
    {
        var expected = _reference.Value.Single(place => place.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var actual = locations.Get(id);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.City, actual.City);
        Assert.Equal(expected.CountryCode, actual.CountryCode);
        Assert.Equal(expected.Latitude, actual.Latitude);
        Assert.Equal(expected.Longitude, actual.Longitude);
        Assert.Equal(expected.TimeZoneId, actual.TimeZoneId);
        Assert.Equal(expected.Population, actual.Population);
        Assert.Equal((expected.Names ?? new Dictionary<string, string>()).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase),
            actual.Names!.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(expected.CountryNames!.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase),
            actual.CountryNames!.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>A missing bundled catalog must fail without creating an empty runtime database.</summary>
    [Fact]
    public void MissingDatabase_IsRejectedWithoutCreatingFiles()
    {
        var directory = Directory.CreateTempSubdirectory("athan-catalog-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "missing.db");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Application:LocationDatabasePath"] = path }).Build();
            Assert.Throws<SqliteException>(() => new LocationRepository(configuration, NullLogger<LocationRepository>.Instance));
            Assert.Empty(directory.EnumerateFileSystemInfos());
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Lookup_FallsBackAndSearchHonorsLimit()
    {
        Assert.Equal("geonames-276781", locations.Get("missing-location").Id);
        Assert.Equal(3, locations.Search("United", "en", 3).Count);
        Assert.Empty(locations.Search("United", "en", 0));
        Assert.Empty(locations.Search("United", "en", -1));
    }

    [Fact]
    public async Task Searches_CanUseIndependentConnectionsConcurrently()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => locations.Search("Beirut", "en"))));
        Assert.All(results, result => Assert.Contains(result, x => x.City == "Beirut"));
    }

    private static int Rank(LocationInfo location, string needle, string language)
    {
        var city = Normalize(location.Name(language));
        var country = Normalize(location.CountryName(language));
        if (city.Equals(needle, StringComparison.OrdinalIgnoreCase) || country.Equals(needle, StringComparison.OrdinalIgnoreCase)
            || location.CountryCode.Equals(needle, StringComparison.OrdinalIgnoreCase)) return 0;
        if (city.StartsWith(needle, StringComparison.OrdinalIgnoreCase) || country.StartsWith(needle, StringComparison.OrdinalIgnoreCase)) return 1;
        if (city.Contains(needle, StringComparison.OrdinalIgnoreCase) || country.Contains(needle, StringComparison.OrdinalIgnoreCase)) return 2;
        return (location.Names?.Values ?? []).Concat(location.CountryNames?.Values ?? [])
            .Any(x => Normalize(x).Contains(needle, StringComparison.OrdinalIgnoreCase)) ? 3 : 4;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) builder.Append(ch);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
