using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using AthanTime.Net.Services;
using Microsoft.Data.Sqlite;

namespace AthanTime.Tests;

/// <summary>Exercises SQLite preferences and translations in isolated profiles without desktop or audio devices.</summary>
public sealed class MovementBreakRepositoryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("athan-movement-").FullName;
    private string DatabasePath => Path.Combine(_directory, "movement-reminders.db");
    private static string SeedPath => Path.Combine(AppContext.BaseDirectory, "Data", "movement-breaks.json");
    private MovementBreakRepository Store => new(DatabasePath, SeedPath);

    [Fact]
    public void NewDatabaseSeedsEightOriginalSuggestionsInEverySupportedLanguage()
    {
        var preferences = Store.Load();
        Assert.False(preferences.Enabled);
        Assert.Equal(30, preferences.IntervalMinutes);
        Assert.Equal(20, preferences.DisplaySeconds);
        Assert.Equal(8, preferences.Practices.Count);
        var languages = new[] { "en", "ar", "fr", "es", "tr", "de", "id", "ur" };
        Assert.All(preferences.Practices, practice =>
        {
            Assert.Equal(languages.Order(), practice.Texts.Keys.Order());
            Assert.Equal(8, practice.Texts.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(languages, language => Assert.Equal(practice.Texts[language], practice.GetText(language.ToUpperInvariant())));
        });
        Assert.Equal(preferences.Practices.Select(item => item.Id), Store.Load().Practices.Select(item => item.Id));
        using var exclusive = new FileStream(DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
    }

    [Fact]
    public void MissingOptionalSidecarsDoNotRaiseDebuggerFileNotFoundExceptions()
    {
        var sidecars = new[] { DatabasePath + "-journal", DatabasePath + "-wal", DatabasePath + "-shm" };
        var observed = 0;
        EventHandler<FirstChanceExceptionEventArgs> handler = (_, args) =>
        {
            if (args.Exception is FileNotFoundException exception
                && sidecars.Any(path => exception.Message.Contains(path, StringComparison.OrdinalIgnoreCase)))
                observed++;
        };
        AppDomain.CurrentDomain.FirstChanceException += handler;
        try { Store.Load(); Store.Load(); }
        finally { AppDomain.CurrentDomain.FirstChanceException -= handler; }
        Assert.Equal(0, observed);
    }

    [Fact]
    public async Task LegacyDatabaseAddsTranslationOrderWithoutLosingCustomData()
    {
        Execute("""
            CREATE TABLE movement_settings (
                id INTEGER PRIMARY KEY CHECK(id=1), enabled INTEGER NOT NULL,
                interval_minutes INTEGER NOT NULL, display_seconds INTEGER NOT NULL);
            CREATE TABLE movement_practices (
                id TEXT PRIMARY KEY COLLATE NOCASE, position INTEGER NOT NULL);
            CREATE TABLE movement_texts (
                practice_id TEXT NOT NULL COLLATE NOCASE REFERENCES movement_practices(id) ON DELETE CASCADE,
                language TEXT NOT NULL COLLATE NOCASE, text TEXT NOT NULL,
                PRIMARY KEY(practice_id, language));
            INSERT INTO movement_settings VALUES(1,1,19,41);
            INSERT INTO movement_practices VALUES('custom',0);
            INSERT INTO movement_texts VALUES('custom','fr','Texte original');
            INSERT INTO movement_texts VALUES('custom','ar','Arabic text');
            PRAGMA user_version=1;
            """);

        // Debug launches can share the same profile and open it concurrently.
        var snapshots = await Task.WhenAll(Task.Run(Store.Load), Task.Run(Store.Load));
        foreach (var loaded in snapshots)
        {
            Assert.True(loaded.Enabled);
            Assert.Equal(19, loaded.IntervalMinutes);
            Assert.Equal(41, loaded.DisplaySeconds);
            var practice = Assert.Single(loaded.Practices);
            Assert.Equal(new[] { "fr", "ar" }, practice.Texts.Keys);
            Assert.Equal("Texte original", practice.GetText("de"));
        }
        Assert.Equal(1L, Scalar("SELECT count(*) FROM pragma_table_info('movement_texts') WHERE name='position'"));

        Store.Save(snapshots[0]);
        Assert.Equal("Texte original", Store.Load().Practices.Single().GetText("de"));
    }

    [Fact]
    public void CustomTranslationsSettingsAndEmptyListSurviveRestartWithoutReseeding()
    {
        Store.Load();
        var preferences = new MovementBreakPreferences
        {
            Enabled = true, IntervalMinutes = 15, DisplaySeconds = 45,
            Practices = [new("user-1", new Dictionary<string, string> { ["AR"] = "نص جديد", ["en"] = "Custom line\n'; DROP TABLE movement_settings; --" })]
        };
        Store.Save(preferences);
        // Existing profiles do not depend on the seed file once their database has been created.
        var restarted = new MovementBreakRepository(DatabasePath, Path.Combine(_directory, "missing-seed.json"));
        var loaded = restarted.Load();
        Assert.True(loaded.Enabled);
        Assert.Equal(15, loaded.IntervalMinutes);
        Assert.Equal(45, loaded.DisplaySeconds);
        Assert.Equal("نص جديد", Assert.Single(loaded.Practices).GetText("ar"));
        Assert.Equal(preferences.Practices[0].GetText("en"), loaded.Practices[0].GetText("unknown"));
        loaded.Practices.Clear();
        restarted.Save(loaded);
        Assert.Empty(restarted.Load().Practices);
        Assert.True(restarted.Load().Enabled);
    }

    [Fact]
    public void TimingBoundsAreStoredWithoutMutatingTheCallersDraft()
    {
        var preferences = new MovementBreakPreferences { IntervalMinutes = 0, DisplaySeconds = int.MaxValue };
        Store.Save(preferences);
        var loaded = Store.Load();
        Assert.Equal(1, loaded.IntervalMinutes);
        Assert.Equal(600, loaded.DisplaySeconds);
        Assert.Equal(0, preferences.IntervalMinutes);
        Assert.Equal(int.MaxValue, preferences.DisplaySeconds);
        Store.Save(new MovementBreakPreferences { IntervalMinutes = int.MaxValue, DisplaySeconds = 0 });
        Assert.Equal(1440, Store.Load().IntervalMinutes);
        Assert.Equal(5, Store.Load().DisplaySeconds);
    }

    [Fact]
    public void BadEditsAreRejectedBeforeChangingExistingPreferences()
    {
        Store.Load();
        var before = SHA256.HashData(File.ReadAllBytes(DatabasePath));
        var bad = new List<MovementBreakPreferences>
        {
            new() { Practices = [new("", new Dictionary<string, string> { ["en"] = "valid" })] },
            new() { Practices = [new("with space", new Dictionary<string, string> { ["en"] = "valid" })] },
            new() { Practices = [new("one", new Dictionary<string, string> { ["en"] = " " })] },
            new() { Practices = [new("one", new Dictionary<string, string> { ["it"] = "unsupported" })] },
            new() { Practices = [new("one", new Dictionary<string, string> { ["en"] = new string('x', 1001) })] },
            new() { Practices = [new("same", new Dictionary<string, string> { ["en"] = "one" }), new("SAME", new Dictionary<string, string> { ["en"] = "two" })] },
            new() { Practices = Enumerable.Range(0, 101).Select(index => new MovementPractice("id-" + index, new Dictionary<string, string> { ["en"] = "text" })).ToList() }
        };
        Assert.All(bad, preferences =>
        {
            Assert.Throws<ArgumentException>(() => Store.Save(preferences));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(DatabasePath)));
        });
    }

    [Fact]
    public void FutureSchemaIsNeitherReadNorDowngraded()
    {
        Store.Load();
        Execute("PRAGMA user_version=99");
        var before = File.ReadAllBytes(DatabasePath);
        Assert.Throws<InvalidDataException>(() => Store.Load());
        Assert.Throws<InvalidDataException>(() => Store.Save(new MovementBreakPreferences()));
        Assert.Equal(before, File.ReadAllBytes(DatabasePath));
    }

    [Fact]
    public void FailedWriteRollsBackBothPreferencesAndTexts()
    {
        var original = Store.Load();
        Execute("CREATE TRIGGER reject_edits BEFORE INSERT ON movement_practices BEGIN SELECT RAISE(ABORT,'fixture failure'); END");
        Assert.Throws<SqliteException>(() => Store.Save(new MovementBreakPreferences
        {
            Enabled = true, IntervalMinutes = 60,
            Practices = [new("custom", new Dictionary<string, string> { ["en"] = "A new suggestion" })]
        }));
        var loaded = Store.Load();
        Assert.False(loaded.Enabled);
        Assert.Equal(30, loaded.IntervalMinutes);
        Assert.Equal(original.Practices.Select(item => item.GetText("en")), loaded.Practices.Select(item => item.GetText("en")));
    }

    [Fact]
    public void MissingSeedDoesNotCreateAnEmptyUserDatabase()
    {
        var path = Path.Combine(_directory, "absent-profile", "movement-reminders.db");
        var store = new MovementBreakRepository(path, Path.Combine(_directory, "missing-seed.json"));
        Assert.Throws<FileNotFoundException>(() => store.Load());
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void FailedInitialMoveRemovesOnlyOwnedStagingAndKeepsExistingFiles()
    {
        Directory.CreateDirectory(DatabasePath);
        File.WriteAllText(Path.Combine(DatabasePath, "keep.txt"), "keep");
        var leftover = DatabasePath + ".older.tmp";
        File.WriteAllText(leftover, "keep");
        Assert.Throws<IOException>(() => Store.Load());
        Assert.Equal("keep", File.ReadAllText(Path.Combine(DatabasePath, "keep.txt")));
        Assert.Equal(new[] { leftover }, Directory.GetFiles(_directory, "movement-reminders.db.*.tmp"));
        Assert.Equal("keep", File.ReadAllText(leftover));
    }

    [Fact]
    public void UnknownUnversionedDatabaseIsPreserved()
    {
        Execute("CREATE TABLE unrelated(value TEXT); INSERT INTO unrelated VALUES('keep')");
        var before = File.ReadAllBytes(DatabasePath);
        Assert.Throws<InvalidDataException>(() => Store.Load());
        Assert.Throws<InvalidDataException>(() => Store.Save(new MovementBreakPreferences()));
        Assert.Equal(before, File.ReadAllBytes(DatabasePath));
    }

    [Fact]
    public void CorruptSavedTextIsBoundedAndReportedInsteadOfSilentlyReplacingIt()
    {
        Store.Load();
        Execute("UPDATE movement_texts SET text='" + new string('x', 1001) + "' WHERE practice_id='walk' AND language='en'");
        var before = File.ReadAllBytes(DatabasePath);
        Assert.Throws<InvalidDataException>(() => Store.Load());
        Assert.Equal(before, File.ReadAllBytes(DatabasePath));
    }

    [Fact]
    public void MalformedSavedTimingIsReportedAsInvalidDataWithoutWriting()
    {
        Store.Load();
        Execute("UPDATE movement_settings SET interval_minutes=9223372036854775807 WHERE id=1");
        var before = File.ReadAllBytes(DatabasePath);
        Assert.Throws<InvalidDataException>(() => Store.Load());
        Assert.Equal(before, File.ReadAllBytes(DatabasePath));
    }

    [Theory]
    [InlineData("UPDATE movement_practices SET position=9 WHERE id='walk'")]
    [InlineData("UPDATE movement_texts SET position=9 WHERE practice_id='walk' AND language='en'")]
    public void MalformedSavedOrderIsReportedWithoutReplacingUserData(string sql)
    {
        Store.Load();
        Execute(sql);
        var before = File.ReadAllBytes(DatabasePath);
        Assert.Throws<InvalidDataException>(() => Store.Load());
        Assert.Equal(before, File.ReadAllBytes(DatabasePath));
    }

    [Fact]
    public void UserTextWithoutEnglishFallsBackToItsAvailableTranslation()
    {
        var practice = new MovementPractice("custom", new Dictionary<string, string> { ["ar"] = "نص المستخدم" });
        Assert.Equal("نص المستخدم", practice.GetText("de"));
        Assert.Equal("نص المستخدم", practice.GetText("AR"));
        var reordered = new MovementPractice("multi", new Dictionary<string, string> { ["fr"] = "Texte", ["ar"] = "نص المستخدم" });
        Assert.Equal("Texte", reordered.GetText("de"));
        Store.Save(new MovementBreakPreferences { Practices = [reordered] });
        Assert.Equal("Texte", Store.Load().Practices.Single().GetText("de"));
    }

    [Fact]
    public void JsonTransferRoundTripsConfigurationTranslationAndFallbackOrderWithoutApplyingOnRead()
    {
        Store.Load();
        var existingHash = SHA256.HashData(File.ReadAllBytes(DatabasePath));
        var snapshot = new MovementBreakPreferences
        {
            Enabled = true,
            IntervalMinutes = 47,
            DisplaySeconds = 85,
            Practices =
            [
                new("custom-one", new Dictionary<string, string> { ["fr"] = "Texte original", ["ar"] = "النص العربي" }),
                new("custom-two", new Dictionary<string, string> { ["en"] = "Walk a little.", ["de"] = "Gehen Sie ein wenig." })
            ]
        };
        var path = Path.Combine(_directory, "movement-backup.json");

        Store.Export(snapshot, path);
        var staged = Store.ReadImport(path);

        Assert.Equal(existingHash, SHA256.HashData(File.ReadAllBytes(DatabasePath)));
        Assert.True(staged.Enabled);
        Assert.Equal(47, staged.IntervalMinutes);
        Assert.Equal(85, staged.DisplaySeconds);
        Assert.Equal(new[] { "custom-one", "custom-two" }, staged.Practices.Select(item => item.Id));
        Assert.Equal(new[] { "fr", "ar" }, staged.Practices[0].Texts.Keys);
        Assert.Equal("Texte original", staged.Practices[0].GetText("de"));

        Store.Save(staged);
        var restarted = Store.Load();
        Assert.Equal("Texte original", restarted.Practices[0].GetText("de"));
        Assert.Equal(new[] { "fr", "ar" }, restarted.Practices[0].Texts.Keys);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Format\":\"AthanTime.MovementBreaks\",\"SchemaVersion\":2,\"Enabled\":true,\"IntervalMinutes\":30,\"DisplaySeconds\":30,\"Practices\":[]}")]
    [InlineData("{\"Format\":\"AthanTime.MovementBreaks\",\"SchemaVersion\":1,\"Enabled\":true,\"IntervalMinutes\":0,\"DisplaySeconds\":30,\"Practices\":[]}")]
    [InlineData("{\"Format\":\"AthanTime.MovementBreaks\",\"SchemaVersion\":1,\"Enabled\":true,\"IntervalMinutes\":30,\"DisplaySeconds\":30,\"Practices\":[{\"Id\":\"one\",\"Texts\":{\"it\":\"unsupported\"}}]}")]
    [InlineData("{\"Format\":\"AthanTime.MovementBreaks\",\"SchemaVersion\":1,\"Enabled\":true,\"IntervalMinutes\":30,\"DisplaySeconds\":30,\"Practices\":[{\"Id\":\"same\",\"Texts\":{\"en\":\"one\"}},{\"Id\":\"SAME\",\"Texts\":{\"en\":\"two\"}}]}")]
    public void InvalidOrFutureJsonImportsNeverChangeTheDatabase(string json)
    {
        Store.Load();
        var before = SHA256.HashData(File.ReadAllBytes(DatabasePath));
        var path = Path.Combine(_directory, "bad-movement-backup.json");
        File.WriteAllText(path, json);

        var failure = Record.Exception(() => Store.ReadImport(path));
        Assert.True(failure is InvalidDataException or System.Text.Json.JsonException, failure?.ToString());
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(DatabasePath)));
    }

    [Fact]
    public void OversizedImportIsRejectedBeforeDeserializationWithoutChangingTheDatabase()
    {
        Store.Load();
        var before = SHA256.HashData(File.ReadAllBytes(DatabasePath));
        var path = Path.Combine(_directory, "large-movement-backup.json");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) stream.SetLength(8 * 1024 * 1024 + 1);

        Assert.Throws<InvalidDataException>(() => Store.ReadImport(path));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(DatabasePath)));
    }

    [Fact]
    public void ExportCannotOverwriteApplicationOwnedFilesOrAnUnrelatedFileWhenValidationFails()
    {
        Store.Load();
        var databaseBefore = File.ReadAllBytes(DatabasePath);
        var protectedPaths = new[]
        {
            DatabasePath, DatabasePath + "-journal", DatabasePath + "-wal", DatabasePath + "-shm",
            DatabasePath + ".owned.tmp", SeedPath
        };
        foreach (var path in protectedPaths)
            Assert.Throws<IOException>(() => Store.Export(new MovementBreakPreferences(), path));
        Assert.Equal(databaseBefore, File.ReadAllBytes(DatabasePath));

        var destinationDirectory = Directory.CreateDirectory(Path.Combine(_directory, "existing-directory"));
        File.WriteAllText(Path.Combine(destinationDirectory.FullName, "keep.txt"), "keep");
        var failure = Record.Exception(() => Store.Export(new MovementBreakPreferences(), destinationDirectory.FullName));
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        Assert.Equal("keep", File.ReadAllText(Path.Combine(destinationDirectory.FullName, "keep.txt")));
        Assert.Empty(Directory.GetFiles(_directory, "existing-directory.*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedProfileOrDatabaseIsRejectedWithoutModifyingTheTarget(bool directoryLink)
    {
        var targetDirectory = Directory.CreateDirectory(Path.Combine(_directory, "external"));
        var targetPath = Path.Combine(targetDirectory.FullName, "movement-reminders.db");
        new MovementBreakRepository(targetPath, SeedPath).Load();
        var before = File.ReadAllBytes(targetPath);
        var link = directoryLink ? Path.Combine(_directory, "profile-link") : DatabasePath;
        try
        {
            if (directoryLink) Directory.CreateSymbolicLink(link, targetDirectory.FullName);
            else File.CreateSymbolicLink(link, targetPath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        { Assert.Skip("Symbolic-link creation requires native permissions."); return; }
        var path = directoryLink ? Path.Combine(link, "movement-reminders.db") : link;
        var store = new MovementBreakRepository(path, SeedPath);
        Assert.Throws<IOException>(() => store.Load());
        Assert.Throws<IOException>(() => store.Save(new MovementBreakPreferences()));
        Assert.Equal(before, File.ReadAllBytes(targetPath));
        if (directoryLink) Directory.Delete(link); else File.Delete(link);
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
