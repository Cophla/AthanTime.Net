using System.IO.Compression;
using System.Text.Json;
using AthanTime.Net.Services;
using Microsoft.Data.Sqlite;

/// <summary>Derives SQLite and its versioned ZIP from archived JSON; the application reads only the published database.</summary>
internal static class LocationDatabase
{
    internal static void Build(string source, string destination)
    {
        // Build alongside the destination, then replace only after a complete transaction.
        var temporary = destination + ".tmp";
        File.Delete(temporary);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = temporary, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=OFF;
                CREATE TABLE metadata(key TEXT PRIMARY KEY, value INTEGER NOT NULL);
                CREATE TABLE countries(code TEXT PRIMARY KEY, names TEXT NOT NULL, normalizedNames TEXT NOT NULL);
                CREATE TABLE locations(id TEXT COLLATE NOCASE UNIQUE NOT NULL, city TEXT NOT NULL,
                    countryCode TEXT NOT NULL, latitude REAL NOT NULL, longitude REAL NOT NULL,
                    timeZoneId TEXT NOT NULL, population INTEGER NOT NULL, names TEXT NOT NULL,
                    normalizedCity TEXT NOT NULL, normalizedCode TEXT NOT NULL,
                    normalizedNames TEXT NOT NULL, alternatives TEXT NOT NULL, search TEXT NOT NULL);
                CREATE VIRTUAL TABLE search_index USING fts5(search, content='locations',
                    content_rowid='rowid', tokenize='trigram case_sensitive 1', detail=none, columnsize=0);
                """;
            command.ExecuteNonQuery();
            using var compressed = File.OpenRead(source);
            using var brotli = new BrotliStream(compressed, CompressionMode.Decompress);
            using var json = JsonDocument.Parse(brotli);
            using var transaction = connection.BeginTransaction();
            command.Transaction = transaction;
            var countries = json.RootElement.GetProperty("countries");
            foreach (var country in countries.EnumerateObject())
            {
                command.CommandText = "INSERT INTO countries VALUES($code,$names,$normalized)";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$code", country.Name);
                command.Parameters.AddWithValue("$names", country.Value.GetRawText());
                command.Parameters.AddWithValue("$normalized", NormalizedNames(country.Value));
                command.ExecuteNonQuery();
            }
            command.CommandText = "INSERT INTO locations VALUES(" +
                string.Join(',', Enumerable.Range(0, 13).Select(i => "$p" + i)) + ")";
            command.Parameters.Clear();
            for (var i = 0; i < 13; i++) command.Parameters.Add(new SqliteParameter("$p" + i, ""));
            command.Prepare();
            var count = 0;
            foreach (var place in json.RootElement.GetProperty("locations").EnumerateArray())
            {
                var city = place.GetProperty("city").GetString()!;
                var code = place.GetProperty("countryCode").GetString()!;
                var hasNames = place.TryGetProperty("names", out var names);
                var alternatives = LocationSearch.Normalize(string.Join('\u001f',
                    (hasNames ? names.EnumerateObject().Select(x => x.Value.GetString()!) : [])
                    .Concat(countries.GetProperty(code).EnumerateObject().Select(x => x.Value.GetString()!))));
                object[] values = [place.GetProperty("id").GetString()!, city, code,
                    place.GetProperty("latitude").GetDouble(), place.GetProperty("longitude").GetDouble(),
                    place.GetProperty("timeZoneId").GetString()!, place.GetProperty("population").GetInt32(),
                    hasNames ? names.GetRawText() : "{}", LocationSearch.Normalize(city), LocationSearch.Normalize(code),
                    hasNames ? NormalizedNames(names) : "{}", alternatives,
                    LocationSearch.Normalize(city + '\u001f' + code + '\u001f' + alternatives)];
                for (var i = 0; i < values.Length; i++) command.Parameters[i].Value = values[i];
                command.ExecuteNonQuery();
                count++;
            }
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO metadata VALUES('count', $count); INSERT INTO search_index(search_index) VALUES('rebuild');";
            command.Parameters.AddWithValue("$count", count);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        File.Move(temporary, destination, overwrite: true);
        var archivePath = destination + ".zip";
        var temporaryArchive = archivePath + ".tmp";
        File.Delete(temporaryArchive);
        try
        {
            using (var archive = ZipFile.Open(temporaryArchive, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("locations.db", CompressionLevel.SmallestSize);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var input = File.OpenRead(destination);
                using var output = entry.Open();
                input.CopyTo(output);
            }
            File.Move(temporaryArchive, archivePath, overwrite: true);
        }
        finally { File.Delete(temporaryArchive); }
    }

    private static string NormalizedNames(JsonElement names) => JsonSerializer.Serialize(
        names.EnumerateObject().ToDictionary(x => x.Name.ToLowerInvariant(), x => LocationSearch.Normalize(x.Value.GetString()!)));
}
