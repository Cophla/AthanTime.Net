using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AthanTime.Net.Services;

/// <summary>One offline movement suggestion and its user-editable language versions.</summary>
public sealed record MovementPractice(string Id, IReadOnlyDictionary<string, string> Texts)
{
    /// <summary>Uses the requested language, English, then the first language entered.</summary>
    public string GetText(string language)
    {
        foreach (var pair in Texts)
            if (pair.Key.Equals(language, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        foreach (var pair in Texts)
            if (pair.Key.Equals("en", StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return Texts.Values.FirstOrDefault() ?? "";
    }
}

/// <summary>Small, device-local break preferences; the feature is opt-in.</summary>
public sealed class MovementBreakPreferences
{
    /// <summary>Enables automatic reminders; new profiles leave this off.</summary>
    public bool Enabled { get; set; }
    /// <summary>Minutes to wait before the next reminder, bounded to 1–1440 when stored.</summary>
    public int IntervalMinutes { get; set; } = 30;
    /// <summary>Seconds a suggestion stays visible, bounded to 5–600 when stored.</summary>
    public int DisplaySeconds { get; set; } = 20;
    /// <summary>Ordered suggestions, with up to 100 records and eight language versions each.</summary>
    public List<MovementPractice> Practices { get; set; } = [];
}

/// <summary>Stores movement data in per-user SQLite and transfers bounded snapshots as JSON.</summary>
public interface IMovementBreakRepository
{
    /// <summary>Initializes original translated suggestions only when creating a new database.</summary>
    MovementBreakPreferences Load();
    /// <summary>Atomically saves the bounded suggestion list and preferences, including an empty list.</summary>
    void Save(MovementBreakPreferences preferences);
    /// <summary>Writes a complete, portable JSON snapshot without changing the active database.</summary>
    void Export(MovementBreakPreferences preferences, string path);
    /// <summary>Reads and validates a JSON snapshot without applying it to the active database.</summary>
    MovementBreakPreferences ReadImport(string path);
}

/// <summary>Uses short-lived, unpooled connections; no SQL or file handle remains active between edits.</summary>
public sealed class MovementBreakRepository(string databasePath, string seedPath) : IMovementBreakRepository
{
    private const int SchemaVersion = 1;
    private const int TransferSchemaVersion = 1;
    private const int MaximumTransferBytes = 8 * 1024 * 1024;
    private const string TransferFormat = "AthanTime.MovementBreaks";
    private static readonly string[] _languages = ["en", "ar", "fr", "es", "tr", "de", "id", "ur"];
    private static readonly JsonSerializerOptions _transferJson = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <inheritdoc />
    public MovementBreakPreferences Load()
    {
        try { return Read(); }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException or InvalidOperationException)
        {
            throw new InvalidDataException("Saved movement data has an invalid value or schema.", exception);
        }
    }

    private MovementBreakPreferences Read()
    {
        CheckPaths();
        var newDatabase = !File.Exists(databasePath);
        var initial = newDatabase ? ReadSeed() : null;
        using var connection = Open(initial, out _);
        // A concurrent Debug instance may save; all three reads must see one snapshot.
        using var transaction = connection.BeginTransaction(deferred: true);
        var preferences = new MovementBreakPreferences();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT enabled, interval_minutes, display_seconds FROM movement_settings WHERE id=1";
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidDataException("Movement preferences are missing.");
            preferences.Enabled = reader.GetInt32(0) != 0;
            preferences.IntervalMinutes = reader.GetInt32(1);
            preferences.DisplaySeconds = reader.GetInt32(2);
        }
        var translations = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            // Bound strings before materializing them, even if an externally edited file is invalid.
            command.CommandText = "SELECT substr(id,1,81), position FROM movement_practices ORDER BY position, id LIMIT 101";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                if (reader.GetInt32(1) != preferences.Practices.Count)
                    throw new InvalidDataException("Movement suggestion order is invalid.");
                var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!translations.TryAdd(id, texts)) throw new InvalidDataException("Movement identifiers are duplicated.");
                preferences.Practices.Add(new MovementPractice(id, texts));
            }
        }
        var count = 0;
        var textPositions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT substr(practice_id,1,81), substr(language,1,3), substr(text,1,1001), position FROM movement_texts ORDER BY practice_id, position, language LIMIT 801";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                var expectedPosition = textPositions.GetValueOrDefault(id);
                if (++count > 800 || reader.GetInt32(3) != expectedPosition
                    || !translations.TryGetValue(id, out var texts)
                    || !texts.TryAdd(reader.GetString(1), reader.GetString(2)))
                    throw new InvalidDataException("Movement translations are invalid or exceed the supported limit.");
                textPositions[id] = expectedPosition + 1;
            }
        }
        try { return Normalize(preferences); }
        catch (ArgumentException exception) { throw new InvalidDataException("Saved movement preferences are invalid.", exception); }
    }

    /// <inheritdoc />
    public void Save(MovementBreakPreferences preferences)
    {
        var validated = Normalize(preferences);
        CheckPaths();
        using var connection = Open(validated, out var initialized);
        if (initialized) return;
        using var transaction = connection.BeginTransaction();
        Write(connection, transaction, validated);
        transaction.Commit();
    }

    /// <inheritdoc />
    public void Export(MovementBreakPreferences preferences, string path)
    {
        var snapshot = Normalize(preferences);
        var destination = ValidateTransferPath(path, forExport: true);
        var document = new MovementTransferDocument
        {
            Format = TransferFormat,
            SchemaVersion = TransferSchemaVersion,
            Enabled = snapshot.Enabled,
            IntervalMinutes = snapshot.IntervalMinutes,
            DisplaySeconds = snapshot.DisplaySeconds,
            Practices = snapshot.Practices
        };
        WriteAtomic(destination, document);
    }

    /// <inheritdoc />
    public MovementBreakPreferences ReadImport(string path)
    {
        var source = ValidateTransferPath(path, forExport: false);
        using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumTransferBytes)
            throw new InvalidDataException("The movement reminder import exceeds the supported size.");
        var document = JsonSerializer.Deserialize<MovementTransferDocument>(stream, _transferJson)
            ?? throw new InvalidDataException("The movement reminder import must be a JSON object.");
        if (!string.Equals(document.Format, TransferFormat, StringComparison.Ordinal)
            || document.SchemaVersion != TransferSchemaVersion
            || document.Enabled is null || document.IntervalMinutes is null || document.DisplaySeconds is null
            || document.IntervalMinutes is < 1 or > 1440 || document.DisplaySeconds is < 5 or > 600
            || document.Practices is null)
            throw new InvalidDataException("The movement reminder import has an unsupported format or schema.");
        try
        {
            return Normalize(new MovementBreakPreferences
            {
                Enabled = document.Enabled.Value,
                IntervalMinutes = document.IntervalMinutes.Value,
                DisplaySeconds = document.DisplaySeconds.Value,
                Practices = document.Practices
            });
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The movement reminder import contains invalid values.", exception);
        }
    }

    private MovementBreakPreferences ReadSeed()
    {
        using var stream = File.OpenRead(seedPath);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("The movement suggestion seed exceeds the supported size.");
        var practices = JsonSerializer.Deserialize<List<MovementPractice>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Movement suggestions are missing.");
        var result = Normalize(new MovementBreakPreferences { Practices = practices });
        if (result.Practices.Count == 0 || result.Practices.Any(practice => practice.Texts.Count != _languages.Length))
            throw new InvalidDataException("Default movement suggestions need all supported translations.");
        return result;
    }

    private SqliteConnection Open(MovementBreakPreferences? initial, out bool initialized)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        CheckPaths();
        initialized = false;
        if (!File.Exists(databasePath))
        {
            if (initial is null) throw new InvalidDataException("The movement database disappeared while loading.");
            initialized = Initialize(initial);
        }
        var connection = CreateConnection(databasePath);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            if (Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != SchemaVersion)
                throw new InvalidDataException("The movement database version is not supported; its contents were kept.");
            UpgradeLegacyTranslationOrder(connection);
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private bool Initialize(MovementBreakPreferences initial)
    {
        if (File.Exists(databasePath)) return false;
        var temporary = databasePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // This call owns only its unique staging file. Crash leftovers are retained,
        // never enumerated or treated as permission to replace another user's data.
        using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        try
        {
            using (var connection = CreateConnection(temporary))
            {
                connection.Open();
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    CREATE TABLE movement_settings (
                        id INTEGER PRIMARY KEY CHECK(id=1), enabled INTEGER NOT NULL,
                        interval_minutes INTEGER NOT NULL, display_seconds INTEGER NOT NULL);
                    CREATE TABLE movement_practices (
                        id TEXT PRIMARY KEY COLLATE NOCASE, position INTEGER NOT NULL UNIQUE CHECK(position>=0));
                    CREATE TABLE movement_texts (
                        practice_id TEXT NOT NULL COLLATE NOCASE REFERENCES movement_practices(id) ON DELETE CASCADE,
                        language TEXT NOT NULL COLLATE NOCASE, text TEXT NOT NULL,
                        position INTEGER NOT NULL CHECK(position>=0),
                        UNIQUE(practice_id, position),
                        PRIMARY KEY(practice_id, language));
                    PRAGMA user_version=1;
                    """;
                command.ExecuteNonQuery();
                Write(connection, transaction, initial);
                transaction.Commit();
            }
            CheckPaths();
            try { File.Move(temporary, databasePath, overwrite: false); return true; }
            catch (IOException) when (File.Exists(databasePath))
            {
                // A concurrent first launch won. Open validates its schema before use.
                return false;
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" }) File.Delete(temporary + suffix);
        }
    }

    private static SqliteConnection CreateConnection(string path) => new(new SqliteConnectionStringBuilder
    {
        DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        DefaultTimeout = 2, ForeignKeys = true
    }.ToString());

    private static void UpgradeLegacyTranslationOrder(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM pragma_table_info('movement_texts') WHERE name = 'position' COLLATE NOCASE";
        if (command.ExecuteScalar() is 1L) return;

        // Early development databases stored translation insertion order only in rowid.
        // Rebuild the small child table transactionally so custom text and fallback order survive.
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        // Another Debug instance may have migrated while this connection waited for the write lock.
        if (command.ExecuteScalar() is 1L) return;
        command.CommandText = """
            CREATE TABLE movement_texts_upgrade (
                practice_id TEXT NOT NULL COLLATE NOCASE REFERENCES movement_practices(id) ON DELETE CASCADE,
                language TEXT NOT NULL COLLATE NOCASE, text TEXT NOT NULL,
                position INTEGER NOT NULL CHECK(position>=0),
                UNIQUE(practice_id, position),
                PRIMARY KEY(practice_id, language));
            INSERT INTO movement_texts_upgrade(practice_id, language, text, position)
                SELECT practice_id, language, text,
                    ROW_NUMBER() OVER(PARTITION BY practice_id ORDER BY rowid) - 1
                FROM movement_texts;
            DROP TABLE movement_texts;
            ALTER TABLE movement_texts_upgrade RENAME TO movement_texts;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void Write(SqliteConnection connection, SqliteTransaction transaction, MovementBreakPreferences preferences)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM movement_texts; DELETE FROM movement_practices; DELETE FROM movement_settings;"
            + "INSERT INTO movement_settings VALUES(1,$enabled,$interval,$duration)";
        command.Parameters.AddWithValue("$enabled", preferences.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$interval", preferences.IntervalMinutes);
        command.Parameters.AddWithValue("$duration", preferences.DisplaySeconds);
        command.ExecuteNonQuery();
        for (var position = 0; position < preferences.Practices.Count; position++)
        {
            var practice = preferences.Practices[position];
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO movement_practices VALUES($id,$position)";
            command.Parameters.AddWithValue("$id", practice.Id);
            command.Parameters.AddWithValue("$position", position);
            command.ExecuteNonQuery();
            var textPosition = 0;
            foreach (var text in practice.Texts)
            {
                command.Parameters.Clear();
                command.CommandText = "INSERT INTO movement_texts VALUES($id,$language,$text,$position)";
                command.Parameters.AddWithValue("$id", practice.Id);
                command.Parameters.AddWithValue("$language", text.Key);
                command.Parameters.AddWithValue("$text", text.Value);
                command.Parameters.AddWithValue("$position", textPosition++);
                command.ExecuteNonQuery();
            }
        }
    }

    private string ValidateTransferPath(string path, bool forExport)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A movement reminder transfer path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        var ownedPaths = new[] { databasePath, databasePath + "-journal", databasePath + "-wal", databasePath + "-shm", seedPath };
        if (ownedPaths.Any(owned => PathsEqual(fullPath, Path.GetFullPath(owned)))
            || fullPath.StartsWith(Path.GetFullPath(databasePath) + ".", PathComparison))
            throw new IOException("Movement reminder transfers cannot use application-owned database or seed paths.");
        if (forExport)
        {
            var directory = Path.GetDirectoryName(fullPath)!;
            if (!Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase)
                || (PathsEqual(directory, Path.GetDirectoryName(Path.GetFullPath(databasePath))!)
                    && Path.GetFileName(fullPath).Equals("settings.json", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Select a JSON backup path outside the active profile files.");
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked movement reminder export folders are not supported.");
            if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked movement reminder export files are not supported.");
        }
        return fullPath;
    }

    private static void WriteAtomic(string path, MovementTransferDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, document, _transferJson);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool PathsEqual(string left, string right) => string.Equals(left, right, PathComparison);

    private sealed class MovementTransferDocument
    {
        public string? Format { get; set; }
        public int SchemaVersion { get; set; }
        public bool? Enabled { get; set; }
        public int? IntervalMinutes { get; set; }
        public int? DisplaySeconds { get; set; }
        public List<MovementPractice>? Practices { get; set; }
    }

    internal static MovementBreakPreferences Normalize(MovementBreakPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.Practices is null || preferences.Practices.Count > 100)
            throw new ArgumentException("Use at most 100 movement suggestions.", nameof(preferences));
        var result = new MovementBreakPreferences
        {
            Enabled = preferences.Enabled,
            IntervalMinutes = Math.Clamp(preferences.IntervalMinutes, 1, 1440),
            DisplaySeconds = Math.Clamp(preferences.DisplaySeconds, 5, 600)
        };
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var practice in preferences.Practices)
        {
            if (practice is null || string.IsNullOrWhiteSpace(practice.Id) || practice.Id.Length > 80
                || practice.Id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
                || !ids.Add(practice.Id) || practice.Texts is null || practice.Texts.Count is < 1 or > 8)
                throw new ArgumentException("Each movement suggestion needs a unique identifier and text.", nameof(preferences));
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var text in practice.Texts)
            {
                if (text.Key is null || !_languages.Contains(text.Key, StringComparer.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(text.Value) || text.Value.Length > 1000
                    || !texts.TryAdd(text.Key.ToLowerInvariant(), text.Value.Trim()))
                    throw new ArgumentException("Movement text needs a supported language and 1–1000 characters.", nameof(preferences));
            }
            result.Practices.Add(new MovementPractice(practice.Id, texts));
        }
        return result;
    }

    private void CheckPaths()
    {
        if (!Path.IsPathFullyQualified(databasePath)) throw new ArgumentException("The movement database path must be absolute.");
        var directory = Path.GetDirectoryName(databasePath)!;
        foreach (var path in new[] { directory, databasePath, databasePath + "-journal", databasePath + "-wal", databasePath + "-shm" })
        {
            if (!Path.Exists(path)) continue;
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked movement database paths are not supported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
