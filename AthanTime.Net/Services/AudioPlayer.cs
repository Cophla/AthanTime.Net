using NVorbis;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Models;
using SoundFlow.Structs;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using Microsoft.Extensions.Logging;

namespace AthanTime.Net.Services;

/// <summary>Plays local Ogg Vorbis recordings; native operations run off the UI thread.</summary>
public interface IAudioPlayer
{
    /// <summary>Whether playback or a playback request is in progress.</summary>
    bool IsBusy { get; }
    /// <summary>Publishes localization keys for playback status; callbacks may use a worker thread.</summary>
    event EventHandler<string>? StatusChanged;
    /// <summary>Replaces current playback, or skips when busy if interruption is disabled.</summary>
    Task PlayAsync(string path, int volume, bool interrupt = true);
    /// <summary>Stops playback without blocking the UI.</summary>
    Task StopAsync();
}

/// <summary>Single-player SoundFlow adapter. NuGet supplies the native backend; no external player is required.</summary>
public sealed class AudioPlayer(ILogger<AudioPlayer> logger) : IAudioPlayer, IDisposable
{
    private readonly ILogger<AudioPlayer> _logger = logger;
    private readonly object _gate = new();
    private MiniAudioEngine? _engine;
    private AudioPlaybackDevice? _device;
    private SoundPlayer? _player;
    private VorbisAudioProvider? _provider;
    private volatile bool _busy;
    private bool _disposed;
    private int _pending;
    private int _generation;

    /// <inheritdoc />
    public bool IsBusy => _busy || Volatile.Read(ref _pending) > 0;
    /// <inheritdoc />
    public event EventHandler<string>? StatusChanged;

    /// <summary>Accepts local .ogg files; the provider validates the Vorbis codec at playback.</summary>
    public static bool IsSupportedFile(string? path) => !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("//", StringComparison.OrdinalIgnoreCase)
        && Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    /// <inheritdoc />
    public async Task PlayAsync(string path, int volume, bool interrupt = true)
    {
        if (!interrupt && IsBusy) return;
        var generation = interrupt ? Interlocked.Increment(ref _generation) : Volatile.Read(ref _generation);
        Interlocked.Increment(ref _pending);
        try
        {
            await Task.Run(() =>
            {
                lock (_gate)
                {
                    if (_disposed || generation != Volatile.Read(ref _generation) || (!interrupt && _busy)) return;
                    try
                    {
                        ReleasePlayback();
                        if (!IsSupportedFile(path))
                        {
                            _logger.LogWarning("Selected audio is not an existing local Ogg recording");
                            Report("AudioFileError");
                            return;
                        }
                        var provider = new VorbisAudioProvider(path);
                        _provider = provider;
                        try
                        {
                            _engine = new MiniAudioEngine();
                            _device = _engine.InitializePlaybackDevice(null, provider.Format);
                            _player = new SoundPlayer(_engine, provider.Format, provider);
                        }
                        catch { provider.Dispose(); throw; }
                        _player.Volume = Math.Clamp(volume, 0, 100) / 100f;
                        _player.PlaybackEnded += OnEnded;
                        _device.MasterMixer.AddComponent(_player);
                        _busy = true;
                        _player.Play();
                        _device.Start();
                        Report("AudioPlaying");
                        _logger.LogInformation("Playing local audio {FileName}", Path.GetFileName(path));
                    }
                    catch (Exception exception)
                    {
                        ReleasePlayback();
                        _logger.LogError(exception, "Ogg Vorbis playback failed; check the recording and output device");
                        Report("AudioError");
                    }
                }
            });
        }
        finally { Interlocked.Decrement(ref _pending); }
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        Interlocked.Increment(ref _generation);
        return Task.Run(() =>
        {
            lock (_gate)
            {
                if (_disposed) return;
                ReleasePlayback();
                Report("AudioStopped");
                _logger.LogDebug("Audio stopped");
            }
        });
    }

    // Cleanup must run outside the native audio callback. A stale completion cannot stop a new prayer.
    private void OnEnded(object? sender, EventArgs args) => _ = Task.Run(() =>
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _player)) return;
            var error = _provider?.Error;
            ReleasePlayback();
            if (error is not null) _logger.LogError(error, "Ogg Vorbis decoding failed during playback");
            Report(error is null ? "AudioStopped" : "AudioError");
        }
    });
    private void Report(string key) => StatusChanged?.Invoke(this, key);

    /// <summary>Releases native resources at application shutdown.</summary>
    public void Dispose()
    {
        Interlocked.Increment(ref _generation);
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ReleasePlayback();
        }
    }

    private void ReleasePlayback()
    {
        // Stop the device before freeing its stream. Idle audio holds no device or decode buffers.
        var device = _device;
        var player = _player;
        var engine = _engine;
        _device = null;
        _player = null;
        _engine = null;
        _provider = null;
        _busy = false;
        try { device?.Stop(); }
        catch (Exception exception) { _logger.LogWarning(exception, "Audio device stop failed"); }
        if (player is not null)
        {
            player.PlaybackEnded -= OnEnded;
            try { device?.MasterMixer.RemoveComponent(player); }
            catch (Exception exception) { _logger.LogWarning(exception, "Audio mixer cleanup failed"); }
            try { player.Dispose(); }
            catch (Exception exception) { _logger.LogWarning(exception, "Audio player cleanup failed"); }
        }
        try { device?.Dispose(); }
        catch (Exception exception) { _logger.LogWarning(exception, "Audio device cleanup failed"); }
        try { engine?.Dispose(); }
        catch (Exception exception) { _logger.LogWarning(exception, "Audio engine cleanup failed"); }
    }
}

/// <summary>Streams Ogg Vorbis samples into SoundFlow without loading a full recording.</summary>
public sealed class VorbisAudioProvider : ISoundDataProvider
{
    private readonly VorbisReader _reader;
    private bool _ended;
    private bool _seekedToEnd;

    /// <summary>Opens a recording using its native sample rate and channel layout.</summary>
    public VorbisAudioProvider(string path)
    {
        var stream = File.OpenRead(path);
        try
        {
            _reader = new VorbisReader(stream, closeOnDispose: true);
            Format = new AudioFormat { Format = SampleFormat.F32, SampleRate = _reader.SampleRate,
                Channels = _reader.Channels, Layout = AudioFormat.GetLayoutFromChannels(_reader.Channels) };
            Length = checked((int)(_reader.TotalSamples * _reader.Channels));
            FormatInfo = new SoundFormatInfo { FormatName = "Ogg Vorbis", FormatIdentifier = "ogg",
                SampleRate = SampleRate, ChannelCount = _reader.Channels, Duration = _reader.TotalTime };
        }
        catch { _reader?.Dispose(); stream.Dispose(); throw; }
    }

    /// <summary>Format shared by the player and output device.</summary>
    public AudioFormat Format { get; }
    /// <summary>Records a decode failure without letting it escape the native audio callback.</summary>
    public Exception? Error { get; private set; }
    /// <inheritdoc />
    public int Position => IsDisposed ? 0 : _seekedToEnd ? Length : checked((int)(_reader.SamplePosition * _reader.Channels));
    /// <inheritdoc />
    public int Length { get; }
    /// <inheritdoc />
    public bool CanSeek => true;
    /// <inheritdoc />
    public SampleFormat SampleFormat => SampleFormat.F32;
    /// <inheritdoc />
    public int SampleRate => Format.SampleRate;
    /// <inheritdoc />
    public bool IsDisposed { get; private set; }
    /// <inheritdoc />
    public SoundFormatInfo? FormatInfo { get; }
    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;
    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <inheritdoc />
    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed || buffer.IsEmpty) return 0;
        if (Error is not null) return 0;
        int count;
        try { count = _seekedToEnd ? 0 : _reader.ReadSamples(buffer); }
        catch (Exception exception) { Error = exception; count = 0; }
        if (count == 0 && !_ended)
        {
            _ended = true;
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
        }
        return count;
    }

    /// <inheritdoc />
    public void Seek(int offset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (offset < 0 || offset > Length || offset % Format.Channels != 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        // SoundFlow counts interleaved samples; NVorbis counts frames (one sample per channel).
        // NVorbis cannot seek to the exclusive end of some valid streams. Represent EOF without reading/buffering it.
        if (offset != Length) _reader.SamplePosition = offset / Format.Channels;
        _seekedToEnd = offset == Length;
        _ended = false;
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(offset));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        _reader.Dispose();
    }
}

/// <summary>Resolves the offline CC0 recordings shipped beside the application.</summary>
public static class BundledAudio
{
    /// <summary>Bundled Athan recording.</summary>
    public static string Athan => Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", "adhan.ogg");

    /// <summary>Short Takbeer recording.</summary>
    public static string Takbeer => Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", "takbeer.ogg");

    /// <summary>Short Tahmid recording.</summary>
    public static string Alhamdulillah => Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", "alhamdulillah.ogg");

    /// <summary>The bundled Adhkar playlist; user recordings can be added separately.</summary>
    public static IReadOnlyList<string> AdhkarFiles => ["bundled:takbeer", "bundled:alhamdulillah"];

    /// <summary>Maps a stable bundled identifier to its installation-local Ogg file.</summary>
    public static string Resolve(string path)
    {
        if (path.Equals("bundled:adhan", StringComparison.OrdinalIgnoreCase)) return Athan;
        if (path.Equals("bundled:takbeer", StringComparison.OrdinalIgnoreCase)) return Takbeer;
        if (path.Equals("bundled:alhamdulillah", StringComparison.OrdinalIgnoreCase)) return Alhamdulillah;
        return path;
    }
}
