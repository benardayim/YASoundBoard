using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using YASoundBoard.Configuration;

namespace YASoundBoard.Library;

public sealed class SoundLibrary
{
    private static readonly JsonSerializerOptions OverrideJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly LibraryOptions _options;
    private readonly AudioOptions _audio;
    private readonly ILogger<SoundLibrary> _log;
    private readonly string _contentRoot;
    private readonly HashSet<string> _extensions;
    private readonly List<Sound> _items = [];
    private readonly Dictionary<string, int> _indexById = new(StringComparer.OrdinalIgnoreCase);

    private int _decoded;
    private int _cached;
    private int _failed;

    public SoundLibrary(
        IOptions<LibraryOptions> options,
        IOptions<AudioOptions> audio,
        IHostEnvironment environment,
        ILogger<SoundLibrary> log)
    {
        _options = options.Value;
        _audio = audio.Value;
        _log = log;
        _contentRoot = environment.ContentRootPath;
        _extensions = new HashSet<string>(_options.Extensions, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Sound> Items => _items;

    public int Count => _items.Count;

    public string SoundsDirectory { get; private set; } = string.Empty;

    public string CacheDirectory { get; private set; } = string.Empty;

    public TimeSpan LoadDuration { get; private set; }

    public int DecodedCount => _decoded;

    public int CachedCount => _cached;

    public int FailedCount => _failed;

    public bool TryGetIndex(string? id, out int index)
    {
        index = -1;
        return !string.IsNullOrEmpty(id) && _indexById.TryGetValue(id, out index);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _items.Clear();
        _indexById.Clear();
        _decoded = 0;
        _cached = 0;
        _failed = 0;

        SoundsDirectory = Paths.Resolve(_contentRoot, _options.SoundsDirectory);
        CacheDirectory = Paths.Resolve(_contentRoot, _options.CacheDirectory);
        Directory.CreateDirectory(SoundsDirectory);

        if (_options.UseCache)
        {
            Directory.CreateDirectory(CacheDirectory);
        }

        var overrides = LoadOverrides();
        var candidates = BuildCandidates();
        var results = new Sound?[candidates.Count];

        if (candidates.Count > 0)
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _options.ParallelDecodes),
                CancellationToken = cancellationToken,
            };

            await Parallel.ForAsync(0, candidates.Count, parallelOptions, async (i, token) =>
            {
                var candidate = candidates[i];
                try
                {
                    results[i] = await DecodeAsync(candidate, overrides, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failed);
                    _log.LogWarning("Skipping {File}: {Message}", candidate.File, ex.Message);
                }
            }).ConfigureAwait(false);
        }

        var loaded = new List<Sound>(results.Length);
        foreach (var sound in results)
        {
            if (sound is not null)
            {
                loaded.Add(sound);
            }
        }

        Sort(loaded);

        _items.AddRange(loaded);
        for (var i = 0; i < _items.Count; i++)
        {
            _indexById[_items[i].Id] = i;
        }

        PruneCache();

        LoadDuration = stopwatch.Elapsed;
        LogSummary();
    }

    private List<Candidate> BuildCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<Candidate>();
        var search = _options.Recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        var files = Directory
            .EnumerateFiles(SoundsDirectory, "*", search)
            .Where(IsSupported)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var relative = Paths.ToPortable(Path.GetRelativePath(SoundsDirectory, file));
            var baseId = Path.ChangeExtension(relative, null) ?? relative;
            var id = baseId;
            var suffix = 2;

            while (!seen.Add(id))
            {
                id = $"{baseId} ({suffix++})";
            }

            list.Add(new Candidate(file, id, Path.GetFileNameWithoutExtension(file)));
        }

        return list;
    }

    private bool IsSupported(string file) => _extensions.Contains(Path.GetExtension(file));

    private async Task<Sound> DecodeAsync(Candidate candidate, Dictionary<string, SoundOverride> overrides, CancellationToken cancellationToken)
    {
        var samples = await LoadSamplesAsync(candidate.File, CachePathFor(candidate.File), cancellationToken).ConfigureAwait(false);

        overrides.TryGetValue(candidate.Id, out var meta);

        var channels = Math.Max(1, _audio.Channels);
        var frames = samples.Length / channels;

        return new Sound
        {
            Id = candidate.Id,
            Name = string.IsNullOrWhiteSpace(meta?.Name) ? candidate.Name : meta!.Name!,
            SourcePath = candidate.File,
            Samples = samples,
            Frames = frames,
            Peak = ComputePeak(samples),
            Duration = _audio.SampleRate > 0 ? (double)frames / _audio.SampleRate : 0,
            Gain = meta?.Gain ?? _options.DefaultGain,
            Hidden = meta?.Hidden ?? false,
            Color = meta?.Color,
            Order = meta?.Order ?? 0,
        };
    }

    private async Task<float[]> LoadSamplesAsync(string file, string cacheFile, CancellationToken cancellationToken)
    {
        if (_options.UseCache && File.Exists(cacheFile))
        {
            var cached = await File.ReadAllBytesAsync(cacheFile, cancellationToken).ConfigureAwait(false);
            if (cached.Length >= sizeof(float))
            {
                Interlocked.Increment(ref _cached);
                return ToFloats(cached);
            }
        }

        var raw = await RunFfmpegAsync(file, cancellationToken).ConfigureAwait(false);
        if (raw.Length < sizeof(float))
        {
            throw new InvalidOperationException("ffmpeg produced no audio.");
        }

        if (_options.UseCache)
        {
            await File.WriteAllBytesAsync(cacheFile, raw, cancellationToken).ConfigureAwait(false);
        }

        Interlocked.Increment(ref _decoded);
        return ToFloats(raw);
    }

    private async Task<byte[]> RunFfmpegAsync(string input, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_options.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(input);

        if (_options.MaxDecodeSeconds > 0)
        {
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add(_options.MaxDecodeSeconds.ToString(CultureInfo.InvariantCulture));
        }

        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("f32le");
        startInfo.ArgumentList.Add("-acodec");
        startInfo.ArgumentList.Add("pcm_f32le");
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add(Math.Max(1, _audio.Channels).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add(Math.Max(8000, _audio.SampleRate).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-");

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffmpeg could not be started.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Could not run '{_options.FfmpegPath}'. Set Library.FfmpegPath to a full path if it is not on PATH.", ex);
        }

        using (process)
        {
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var output = new MemoryStream();

            await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var error = await errorTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(error.Trim() is { Length: > 0 } message ? message : $"ffmpeg exited with code {process.ExitCode}.");
            }

            return output.ToArray();
        }
    }

    private string CachePathFor(string file)
    {
        var info = new FileInfo(file);
        var stamp = string.Create(
            CultureInfo.InvariantCulture,
            $"{file}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{_audio.SampleRate}|{_audio.Channels}|{_options.MaxDecodeSeconds}");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)));
        return Path.Combine(CacheDirectory, hash[..32] + ".f32");
    }

    private Dictionary<string, SoundOverride> LoadOverrides()
    {
        var empty = new Dictionary<string, SoundOverride>(StringComparer.OrdinalIgnoreCase);
        var path = Paths.Resolve(SoundsDirectory, _options.MetadataFile);

        if (!File.Exists(path))
        {
            return empty;
        }

        try
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, SoundOverride>>(json, OverrideJson);
            if (parsed is null || parsed.Count == 0)
            {
                return empty;
            }

            _log.LogInformation("Applying {Count} override(s) from {File}", parsed.Count, Path.GetFileName(path));
            return new Dictionary<string, SoundOverride>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Ignoring {File}: {Message}", path, ex.Message);
            return empty;
        }
    }

    private void Sort(List<Sound> items)
    {
        Comparison<Sound> comparison = _options.Sort.ToLowerInvariant() switch
        {
            "order" => (a, b) =>
            {
                var byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            },
            "path" => (a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase),
            "duration" => (a, b) => b.Duration.CompareTo(a.Duration),
            "peak" => (a, b) => b.Peak.CompareTo(a.Peak),
            _ => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
        };

        items.Sort(comparison);
    }

    private void PruneCache()
    {
        if (!_options.UseCache || !Directory.Exists(CacheDirectory))
        {
            return;
        }

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sound in _items)
        {
            expected.Add(Path.GetFileName(CachePathFor(sound.SourcePath)));
        }

        var stale = new List<string>();
        foreach (var file in Directory.EnumerateFiles(CacheDirectory, "*.f32"))
        {
            if (!expected.Contains(Path.GetFileName(file)))
            {
                stale.Add(file);
            }
        }

        if (stale.Count == 0)
        {
            return;
        }

        if (!_options.PruneCache)
        {
            _log.LogInformation(
                "{Count} stale cache file(s) in {Directory}. Set Library.PruneCache to true to remove them automatically.",
                stale.Count,
                CacheDirectory);
            return;
        }

        var removed = 0;
        foreach (var file in stale)
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch
            {
                // A locked or already-removed entry is not worth failing the load over.
            }
        }

        _log.LogInformation("Removed {Count} stale cache file(s) from {Directory}", removed, CacheDirectory);
    }

    private void LogSummary()
    {
        if (_items.Count == 0)
        {
            if (_failed > 0)
            {
                _log.LogError(
                    "Every sound failed to decode, so the library is empty. Check that Library.FfmpegPath (currently \"{Ffmpeg}\") points at a working ffmpeg.",
                    _options.FfmpegPath);
            }
            else
            {
                _log.LogWarning(
                    "No sounds found. Drop audio files into {Directory} and restart, or point Library.SoundsDirectory somewhere else.",
                    SoundsDirectory);
            }

            return;
        }

        long bytes = 0;
        foreach (var sound in _items)
        {
            bytes += (long)sound.Samples.Length * sizeof(float);
        }

        _log.LogInformation(
            "Loaded {Count} sound(s) from {Directory} in {Elapsed:0.00}s ({Decoded} decoded, {Cached} from cache, {Failed} failed, {Megabytes:0.0} MB in RAM)",
            _items.Count,
            SoundsDirectory,
            LoadDuration.TotalSeconds,
            _decoded,
            _cached,
            _failed,
            bytes / 1024.0 / 1024.0);
    }

    private static float[] ToFloats(byte[] raw)
    {
        var usable = raw.Length - (raw.Length % sizeof(float));
        var result = new float[usable / sizeof(float)];
        Buffer.BlockCopy(raw, 0, result, 0, usable);
        return result;
    }

    private static float ComputePeak(float[] samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            var magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return peak;
    }

    private readonly record struct Candidate(string File, string Id, string Name);
}
