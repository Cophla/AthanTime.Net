using System.IO.Compression;
using AthanTime.Net.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AthanTime.Tests;

/// <summary>Checks real Vorbis decoding and user-controlled email export without devices or mail clients.</summary>
public sealed class AudioAndSupportTests(SupportService support, ILocalizationService localization,
    ILogger<AudioPlayer> logger)
{
    [Fact]
    public void EmptyDecodeReadDoesNotAdvanceOrEndTheRecording()
    {
        using var provider = new VorbisAudioProvider(BundledAudio.Athan);
        int ended = 0;
        provider.EndOfStreamReached += (_, _) => ended++;
        Assert.Equal(0, provider.ReadBytes(Span<float>.Empty));
        Assert.Equal(0, provider.Position);
        Assert.Equal(0, ended);
        Assert.True(provider.ReadBytes(new float[4096]) > 0);
        Assert.Null(provider.Error);
    }

    [Fact]
    public void DecoderValidatesSeekBoundariesAndDisposesIdempotently()
    {
        using var provider = new VorbisAudioProvider(BundledAudio.Athan);
        Assert.True(provider.CanSeek);
        var positions = new List<int>();
        int ended = 0;
        provider.EndOfStreamReached += (_, _) => ended++;
        provider.PositionChanged += (_, args) => positions.Add(args.NewPosition);
        int offset = provider.Format.Channels;
        provider.Seek(offset);
        Assert.Equal(offset, provider.Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.Seek(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => provider.Seek(provider.Length + 1));
        Assert.Equal(offset, provider.Position);
        Assert.Equal(new[] { offset }, positions);
        provider.Seek(provider.Length);
        Assert.Equal(provider.Length, provider.Position);
        Assert.Equal(0, ended);
        Assert.Equal(0, provider.ReadBytes(new float[4096]));
        Assert.Equal(1, ended);
        Assert.Equal(0, provider.ReadBytes(new float[4096]));
        Assert.Equal(1, ended);
        provider.Seek(0);
        Assert.True(provider.ReadBytes(new float[4096]) > 0);
        provider.Dispose();
        provider.Dispose();
        Assert.True(provider.IsDisposed);
        Assert.Equal(0, provider.Position);
        Assert.Equal(0, provider.ReadBytes(new float[4096]));
        Assert.Throws<ObjectDisposedException>(() => provider.Seek(0));
    }

    [Fact]
    public void BundledOggSamplesDecodeCompletelyWithoutAudioDevices()
    {
        foreach (var path in new[] { BundledAudio.Athan, BundledAudio.Takbeer, BundledAudio.Alhamdulillah })
        {
            using var provider = new VorbisAudioProvider(path);
            var buffer = new float[4096];
            var total = 0;
            var hasSound = false;
            var ended = 0;
            provider.EndOfStreamReached += (_, _) => ended++;
            int count;
            while ((count = provider.ReadBytes(buffer)) > 0)
            {
                for (var index = 0; index < count; index++)
                {
                    Assert.True(float.IsFinite(buffer[index]));
                    hasSound |= Math.Abs(buffer[index]) > 0.001f;
                }
                total += count;
            }
            Assert.Equal(provider.Length, total);
            Assert.True(hasSound);
            Assert.Equal(1, ended);
            Assert.Equal(0, provider.ReadBytes(buffer));
            Assert.Equal(1, ended);
            provider.Seek(0);
            Assert.Equal(0, provider.Position);
            Assert.True(provider.ReadBytes(buffer) > 0);
        }
    }

    [Fact]
    public async Task MalformedOggFailsBeforeOpeningAnOutputDevice()
    {
        var directory = Directory.CreateTempSubdirectory("athan-audio-test-").FullName;
        try
        {
            var path = Path.Combine(directory, "broken.ogg");
            File.WriteAllText(path, "not an Ogg recording");
            using var player = new AudioPlayer(logger);
            var statuses = new List<string>();
            player.StatusChanged += (_, status) => statuses.Add(status);
            await player.PlayAsync(path, 80);
            Assert.Contains("AudioError", statuses);
            Assert.False(player.IsBusy);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExportIncludesOnlyOwnLogsAndDisabledLoggingProducesNothing()
    {
        var directory = Directory.CreateTempSubdirectory("athan-export-test-").FullName;
        try
        {
            using var sink = new FileLogSink(directory);
            sink.SetEnabled(true);
            using var fileLogger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
            fileLogger.Debug("Diagnostic export test");
            File.WriteAllText(Path.Combine(directory, "Logs", "private.txt"), "Do not export");
            using var destination = new MemoryStream();
            Assert.True(sink.Export(destination));
            destination.Position = 0;
            using (var zip = new ZipArchive(destination, ZipArchiveMode.Read, leaveOpen: true))
            {
                var entry = Assert.Single(zip.Entries);
                Assert.StartsWith("athan-", entry.Name);
                using var reader = new StreamReader(entry.Open());
                Assert.Contains("Diagnostic export test", reader.ReadToEnd());
            }
            sink.SetEnabled(false);
            using var empty = new MemoryStream();
            Assert.False(sink.Export(empty));
            Assert.Equal(0, empty.Length);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(SupportMessage.DebugLogs)]
    [InlineData(SupportMessage.Suggestion)]
    [InlineData(SupportMessage.Complaint)]
    public void DraftUsesUserMailAccountAndEncodesLocalizedFields(SupportMessage message)
    {
        foreach (var language in LocalizationService.Languages)
        {
            var uri = support.CreateDraft(message, language.Code.ToUpperInvariant(), "logs & test.zip");
            Assert.Equal("mailto", uri.Scheme);
            Assert.StartsWith("mailto:athantimedotnet@gmail.com?", uri.OriginalString, StringComparison.Ordinal);
            var decoded = Uri.UnescapeDataString(uri.OriginalString);
            Assert.Contains($"Athan Time {AthanTime.Net.App.ReleaseVersion}", decoded);
            Assert.Contains(localization.Get(language.Code, "EmailBody"), decoded);
            Assert.Contains("logs & test.zip", decoded);
            Assert.Contains("%26", uri.OriginalString);
            Assert.DoesNotContain("&attachment=", uri.OriginalString, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Check the actual shipped JSON too, so a correct fallback cannot hide a stale package destination.</summary>
    [Theory]
    [InlineData(SupportMessage.DebugLogs)]
    [InlineData(SupportMessage.Suggestion)]
    [InlineData(SupportMessage.Complaint)]
    public void ShippedConfigurationUsesThePublicSupportMailbox(SupportMessage message)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build();
        Assert.Equal("athantimedotnet@gmail.com", configuration["Application:SupportEmail"]);
        var configured = new SupportService(localization, configuration);
        foreach (var language in LocalizationService.Languages)
            Assert.StartsWith("mailto:athantimedotnet@gmail.com?", configured.CreateDraft(message, language.Code).OriginalString,
                StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitSupportRecipientOverridesTheFallback()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Application:SupportEmail"] = "feedback@example.org" }).Build();
        var configured = new SupportService(localization, configuration);
        Assert.StartsWith("mailto:feedback@example.org?", configured.CreateDraft(SupportMessage.Suggestion, "en").OriginalString,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("recipient@")]
    public void InvalidConfiguredRecipientCannotCreateAMailDraft(string recipient)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Application:SupportEmail"] = recipient }).Build();
        var configured = new SupportService(localization, configuration);
        Assert.Throws<FormatException>(() => configured.CreateDraft(SupportMessage.Complaint, "en"));
    }
}
