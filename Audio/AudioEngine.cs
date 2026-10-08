using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using YasoundBoard.Configuration;
using YasoundBoard.Library;

namespace YasoundBoard.Audio;

public sealed class AudioEngine : IDisposable
{
    private readonly AudioOptions _options;
    private readonly SoundLibrary _library;
    private readonly ILogger<AudioEngine> _log;

    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private WasapiPlayer? _player;
    private bool _disposed;

    public AudioEngine(IOptions<AudioOptions> options, SoundLibrary library, ILogger<AudioEngine> log)
    {
        _options = options.Value;
        _library = library;
        _log = log;
    }

    public VoiceMixer Mixer { get; private set; } = null!;

    public bool IsRunning => _player?.PlaybackState == PlaybackState.Playing;

    public string DeviceName { get; private set; } = string.Empty;

    public string ShareMode { get; private set; } = string.Empty;

    public bool LowLatencyActive { get; private set; }

    public double ActualLatencyMs { get; private set; }

    public WaveFormat Format => Mixer.WaveFormat;

    public static IReadOnlyList<string> ListRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var names = new List<string>();

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            names.Add(device.FriendlyName);
            device.Dispose();
        }

        return names;
    }

    public void Start()
    {
        Mixer = new VoiceMixer(_options, _library);

        _device = ResolveDevice();
        DeviceName = _device.FriendlyName;

        var shareMode = ParseShareMode(_options.ShareMode);

        try
        {
            _player = CreatePlayer(_device, shareMode);
        }
        catch (Exception ex) when (shareMode == AudioClientShareMode.Exclusive && _options.FallBackToShared)
        {
            _log.LogWarning("Exclusive mode was refused ({Message}); falling back to shared mode.", ex.Message);

            shareMode = AudioClientShareMode.Shared;
            _player = CreatePlayer(_device, shareMode);
        }

        ShareMode = shareMode.ToString();
        LowLatencyActive = _player.LowLatencyActive;
        ActualLatencyMs = _player.LatencyMilliseconds;

        _player.Play();

        _log.LogInformation(
            "Rendering to \"{Device}\" ({Mode}, {Rate} Hz, {Channels} ch, {Voices} voice slots)",
            DeviceName,
            ShareMode,
            Mixer.WaveFormat.SampleRate,
            Mixer.WaveFormat.Channels,
            _options.MaxVoices);

        if (shareMode == AudioClientShareMode.Shared && _options.LowLatency)
        {
            if (LowLatencyActive)
            {
                _log.LogInformation("Low-latency shared mode is active, engine period {Latency} ms", ActualLatencyMs);
            }
            else
            {
                _log.LogWarning(
                    "Low latency was requested but not granted, so standard shared mode is in use: {Reason}",
                    _player.LowLatencyUnavailableReason ?? "no reason reported");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _player?.Stop();
        }
        catch (Exception ex)
        {
            _log.LogDebug("Stopping the output stream failed: {Message}", ex.Message);
        }

        _player?.Dispose();
        _device?.Dispose();
        _enumerator?.Dispose();
    }

    private WasapiPlayer CreatePlayer(MMDevice device, AudioClientShareMode shareMode)
    {
        var builder = new WasapiPlayerBuilder().WithDevice(device);

        if (shareMode == AudioClientShareMode.Exclusive)
        {
            builder.WithExclusiveMode();
        }
        else
        {
            builder.WithSharedMode();
        }

        builder.WithLatency(Math.Max(1, _options.LatencyMs));

        if (_options.UseEventSync)
        {
            builder.WithEventSync();
        }
        else
        {
            builder.WithPollingSync();
        }

        if (!string.IsNullOrWhiteSpace(_options.MmcssThreadPriority))
        {
            builder.WithMmcssThreadPriority(_options.MmcssThreadPriority);
        }

        if (_options.RawMode)
        {
            builder.WithRawMode();
        }

        if (Enum.TryParse<AudioStreamCategory>(_options.Category, true, out var category))
        {
            builder.WithCategory(category);
        }

        if (shareMode == AudioClientShareMode.Shared && _options.LowLatency)
        {
            builder.WithLowLatency(_options.LowLatencyRequired);
        }

        var player = builder.Build();
        player.Init(Mixer);
        return player;
    }

    private MMDevice ResolveDevice()
    {
        _enumerator = new MMDeviceEnumerator();

        var wanted = new List<string>();
        if (!string.IsNullOrWhiteSpace(_options.DeviceNameMatch))
        {
            wanted.Add(_options.DeviceNameMatch);
        }

        foreach (var fallback in _options.DeviceNameFallbacks)
        {
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                wanted.Add(fallback);
            }
        }

        var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

        foreach (var name in wanted)
        {
            foreach (var device in devices)
            {
                if (!device.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var other in devices)
                {
                    if (!ReferenceEquals(other, device))
                    {
                        other.Dispose();
                    }
                }

                _log.LogInformation("Matched output device \"{Device}\" on pattern \"{Pattern}\"", device.FriendlyName, name);
                return device;
            }
        }

        var available = devices.Count == 0
            ? "(none)"
            : string.Join(", ", devices.Select(d => $"\"{d.FriendlyName}\""));

        foreach (var device in devices)
        {
            device.Dispose();
        }

        throw new InvalidOperationException(
            $"No active render device matched \"{_options.DeviceNameMatch}\". Available: {available}. " +
            "Set Audio.DeviceNameMatch or Audio.DeviceNameFallbacks in appsettings.json.");
    }

    private static AudioClientShareMode ParseShareMode(string value)
        => value.Equals("Exclusive", StringComparison.OrdinalIgnoreCase)
            ? AudioClientShareMode.Exclusive
            : AudioClientShareMode.Shared;
}
