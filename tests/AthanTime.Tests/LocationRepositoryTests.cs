using AthanTime.Net.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AthanTime.Tests;

/// <summary>Verifies global SQLite data coverage and case-insensitive search behavior.</summary>
public sealed class LocationRepositoryTests(ILocationRepository locations)
{
    private readonly ILocationRepository _locations = locations;

    /// <summary>A missing catalog must fail visibly without creating an empty database or importing obsolete seeds.</summary>
    [Fact]
    public void MissingDatabaseDoesNotCreateAnEmptyCatalogOrFallback()
    {
        var directory = Directory.CreateTempSubdirectory("athan-missing-catalog-");
        try
        {
            var path = Path.Combine(directory.FullName, "locations.db");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Application:LocationDatabasePath"] = path }).Build();
            var error = Assert.Throws<SqliteException>(() => new LocationRepository(configuration, NullLogger<LocationRepository>.Instance));
            Assert.Equal(14, error.SqliteErrorCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory.FullName));
        }
        finally { directory.Delete(); }
    }

    /// <summary>Checks that the complete generated offline catalog is shipped.</summary>
    [Fact]
    public void Count_ContainsGlobalOfflineIndex() => Assert.True(_locations.Count >= 200_000);

    /// <summary>Checks identifiers, city names, country names, and language codes without case sensitivity.</summary>
    [Theory]
    [InlineData("BEIRUT", "EN", "Beirut")]
    [InlineData("بيروت", "AR", "Beirut")]
    [InlineData("tUrQuIe", "FR", null)]
    public void Search_IsCaseInsensitive(string query, string language, string? expectedCity)
    {
        var results = _locations.Search(query, language);

        Assert.NotEmpty(results);
        if (expectedCity is not null)
            Assert.Contains(results, item => item.City.Equals(expectedCity, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Checks case-insensitive identifier lookup.</summary>
    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var lower = _locations.Get("geonames-276781");
        var upper = _locations.Get("GEONAMES-276781");

        Assert.Equal(lower.Id, upper.Id, ignoreCase: true);
    }
}
