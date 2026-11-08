using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

const string baseUrl = "https://download.geonames.org/export/dump/";
string[] languages = ["en", "ar", "fr", "es", "tr", "de", "id", "ur"];
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var cache = Path.Combine(root, ".location-cache");
var output = Path.Combine(root, "AthanTime.Net", "Data", "locations.json.br");
var database = Path.ChangeExtension(Path.ChangeExtension(output, null), ".db");
if (args.Contains("--database-only", StringComparer.OrdinalIgnoreCase))
{
    LocationDatabase.Build(output, database);
    Log.Information("Rebuilt offline SQLite database {Database} and its versioned ZIP", database);
    await Log.CloseAndFlushAsync();
    return;
}
Directory.CreateDirectory(cache);

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
await Download("cities500.zip");
await Download("alternateNamesV2.zip");
await Download("countryInfo.txt");

var countries = ReadCountries(Path.Combine(cache, "countryInfo.txt"));
var cities = ReadCities(Path.Combine(cache, "cities500.zip"));
var targetIds = cities.Select(x => x.GeoNameId).Concat(countries.Values.Select(x => x.GeoNameId)).ToHashSet();
var names = ReadNames(
    Path.Combine(cache, "alternateNamesV2.zip"),
    targetIds,
    languages.ToHashSet(StringComparer.OrdinalIgnoreCase));

foreach (var country in countries.Values)
{
    country.Names["en"] = country.EnglishName;
    if (names.TryGetValue(country.GeoNameId, out var translated))
        foreach (var pair in translated) country.Names[pair.Key] = pair.Value.Name;
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
{
    await using var outputStream = File.Create(output);
    await using var brotli = new BrotliStream(outputStream, CompressionLevel.SmallestSize);
    using var json = new Utf8JsonWriter(brotli);
    json.WriteStartObject();
    json.WritePropertyName("countries");
    json.WriteStartObject();
    foreach (var country in countries.OrderBy(x => x.Key))
    {
        json.WritePropertyName(country.Key);
        JsonSerializer.Serialize(json, country.Value.Names);
    }
    json.WriteEndObject();
    json.WritePropertyName("locations");
    json.WriteStartArray();
    foreach (var city in cities.OrderByDescending(x => x.Population))
    {
        json.WriteStartObject();
        json.WriteString("id", $"geonames-{city.GeoNameId}");
        json.WriteString("city", city.Name);
        json.WriteString("countryCode", city.CountryCode);
        json.WriteNumber("latitude", city.Latitude);
        json.WriteNumber("longitude", city.Longitude);
        json.WriteString("timeZoneId", city.TimeZoneId);
        json.WriteNumber("population", city.Population);
        if (names.TryGetValue(city.GeoNameId, out var translated))
        {
            json.WritePropertyName("names");
            json.WriteStartObject();
            foreach (var pair in translated) json.WriteString(pair.Key, pair.Value.Name);
            json.WriteEndObject();
        }
        json.WriteEndObject();
    }
    json.WriteEndArray();
    json.WriteEndObject();
    json.Flush();
}
LocationDatabase.Build(output, database);
Log.Information("Wrote {CityCount} places and {CountryCount} countries to {Output}, SQLite and its versioned ZIP", cities.Count, countries.Count, output);
await Log.CloseAndFlushAsync();

async Task Download(string file)
{
    var path = Path.Combine(cache, file);
    if (File.Exists(path) && new FileInfo(path).Length > 0) return;
    Log.Information("Downloading {File}", file);
    await using var source = await http.GetStreamAsync(baseUrl + file);
    await using var destination = File.Create(path);
    await source.CopyToAsync(destination);
}

static Dictionary<string, Country> ReadCountries(string path)
{
    var result = new Dictionary<string, Country>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadLines(path))
    {
        if (line.StartsWith('#')) continue;
        var fields = line.Split('\t');
        if (fields.Length < 17 || !int.TryParse(fields[16], out var id)) continue;
        result[fields[0]] = new Country(id, fields[4], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
    return result;
}

static List<City> ReadCities(string zipPath)
{
    var result = new List<City>(220_000);
    using var archive = ZipFile.OpenRead(zipPath);
    using var reader = new StreamReader(archive.Entries.Single(x =>
        x.Name.Equals("cities500.txt", StringComparison.OrdinalIgnoreCase)).Open());
    while (reader.ReadLine() is { } line)
    {
        var f = line.Split('\t');
        if (f.Length < 18 || string.IsNullOrWhiteSpace(f[17])) continue;
        result.Add(new City(
            int.Parse(f[0], CultureInfo.InvariantCulture), f[1], f[8],
            double.Parse(f[4], CultureInfo.InvariantCulture), double.Parse(f[5], CultureInfo.InvariantCulture),
            f[17], long.TryParse(f[14], out var population) ? (int)Math.Min(population, int.MaxValue) : 0));
    }
    return result;
}

static Dictionary<int, Dictionary<string, PreferredName>> ReadNames(
    string zipPath, HashSet<int> targets, HashSet<string> languages)
{
    var result = new Dictionary<int, Dictionary<string, PreferredName>>();
    using var archive = ZipFile.OpenRead(zipPath);
    using var reader = new StreamReader(archive.Entries.Single(x =>
        x.Name.Equals("alternateNamesV2.txt", StringComparison.OrdinalIgnoreCase)).Open());
    long count = 0;
    while (reader.ReadLine() is { } line)
    {
        if (++count % 5_000_000 == 0) Log.Information("Read {NameCount} alternate names", count);
        var f = line.Split('\t');
        if (f.Length < 4 || !int.TryParse(f[1], out var id) || !targets.Contains(id) || !languages.Contains(f[2])) continue;
        var preferred = f.Length > 4 && f[4].Equals("1", StringComparison.OrdinalIgnoreCase);
        if (!result.TryGetValue(id, out var byLanguage))
            result[id] = byLanguage = new Dictionary<string, PreferredName>(StringComparer.OrdinalIgnoreCase);
        if (!byLanguage.TryGetValue(f[2], out var existing) || preferred && !existing.IsPreferred)
            byLanguage[f[2]] = new PreferredName(f[3], preferred);
    }
    return result;
}

sealed record City(int GeoNameId, string Name, string CountryCode, double Latitude, double Longitude, string TimeZoneId, int Population);
sealed record Country(int GeoNameId, string EnglishName, Dictionary<string, string> Names);
sealed record PreferredName(string Name, bool IsPreferred);
