using System.Text.Json;
using AthanTime.Net.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.Services;

/// <summary>Provides case-insensitive search over the bundled offline location database.</summary>
public interface ILocationRepository
{
    /// <summary>Gets the number of bundled offline places.</summary>
    int Count { get; }
    /// <summary>Gets a place by identifier using a case-insensitive comparison.</summary>
    LocationInfo Get(string id);
    /// <summary>Searches localized place and country names case-insensitively.</summary>
    IReadOnlyList<LocationInfo> Search(string? query, string language, int limit = 30);
}

/// <inheritdoc />
public sealed class LocationRepository : ILocationRepository
{
    private readonly string _connectionString;
    private readonly ILogger<LocationRepository> _logger;

    /// <summary>Opens the prebuilt data read-only; never imports the dataset at runtime.</summary>
    public LocationRepository(IConfiguration configuration, ILogger<LocationRepository> logger)
    {
        _logger = logger;
        var path = Path.GetFullPath(configuration["Application:LocationDatabasePath"]
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "locations.db"));
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key = 'count'";
        Count = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        _logger.LogInformation("Opened {LocationCount} offline places from SQLite {DatabasePath}", Count, path);
    }

    /// <inheritdoc />
    public int Count { get; }

    /// <inheritdoc />
    public LocationInfo Get(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns + """
             FROM locations l JOIN countries c ON c.code = l.countryCode
             WHERE l.id = $id COLLATE NOCASE
            """;
        command.Parameters.AddWithValue("$id", id);
        using (var reader = command.ExecuteReader())
            if (reader.Read()) return Read(reader);
        // Keep the established deterministic fallback for legacy missing identifiers.
        if (!id.Equals("geonames-276781", StringComparison.OrdinalIgnoreCase)) return Get("geonames-276781");
        throw new InvalidOperationException("The offline database is missing its default location.");
    }

    /// <inheritdoc />
    public IReadOnlyList<LocationInfo> Search(string? query, string language, int limit = 30)
    {
        var needle = LocationSearch.Normalize(query ?? "");
        if (needle.Length < 2 || limit <= 0) return [];
        using var connection = Open();
        using var command = connection.CreateCommand();
        // GLOB escapes preserve literal user text. Trigrams accelerate >=3-character
        // substrings; shorter queries scan disk data without loading the catalog.
        var filter = needle.EnumerateRunes().Count() >= 3
            ? "l.rowid IN (SELECT rowid FROM search_index WHERE search GLOB $pattern)"
            : "instr(l.search, $needle) > 0";
        command.CommandText = """
            WITH candidates AS (
                SELECT l.rowid AS locationRow,
                    coalesce(json_extract(l.normalizedNames, $language), l.normalizedCity) AS city,
                    coalesce(json_extract(c.normalizedNames, $language), l.normalizedCode) AS country,
                    l.normalizedCode AS code, l.alternatives AS alternatives
                FROM locations l JOIN countries c ON c.code = l.countryCode
            """ + " WHERE " + filter + """
            ), ranked AS (
                SELECT locationRow, CASE
                    WHEN city = $needle OR country = $needle OR code = $needle THEN 0
                    WHEN substr(city, 1, length($needle)) = $needle OR substr(country, 1, length($needle)) = $needle THEN 1
                    WHEN instr(city, $needle) > 0 OR instr(country, $needle) > 0 THEN 2
                    WHEN instr(alternatives, $needle) > 0 THEN 3 ELSE 4 END AS rank
                FROM candidates
            )
            """ + SelectColumns + """
             FROM ranked r JOIN locations l ON l.rowid = r.locationRow
             JOIN countries c ON c.code = l.countryCode
             WHERE r.rank < 4 ORDER BY r.rank, l.population DESC, l.rowid LIMIT $limit
            """;
        var lang = language.ToLowerInvariant();
        command.Parameters.AddWithValue("$language", "$." + (lang is "en" or "ar" or "fr" or "es" or "tr" or "de" or "id" or "ur" ? lang : "unknown"));
        command.Parameters.AddWithValue("$needle", needle);
        command.Parameters.AddWithValue("$pattern", "*" + string.Concat(needle.Select(ch => ch switch
        {
            '*' => "[*]", '?' => "[?]", '[' => "[[]", _ => ch.ToString()
        })) + "*");
        command.Parameters.AddWithValue("$limit", limit);
        var results = new List<LocationInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(Read(reader));
        _logger.LogDebug("Offline search for {Query} in {Language} returned {ResultCount} places", query, language, results.Count);
        return results;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA cache_size=-2048; PRAGMA query_only=ON;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private const string SelectColumns = """
        SELECT l.id, l.city, l.countryCode, l.latitude, l.longitude, l.timeZoneId,
               l.names, c.names, l.population
        """;

    private static LocationInfo Read(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3),
        reader.GetDouble(4), reader.GetString(5), Names(reader.GetString(6)),
        Names(reader.GetString(7)), reader.GetInt32(8));

    private static Dictionary<string, string> Names(string json) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string>>(json)!, StringComparer.OrdinalIgnoreCase);
}
