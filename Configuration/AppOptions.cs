namespace YASoundBoard.Configuration;

public sealed class AudioOptions
{
    public const string SectionName = "Audio";

    public string DeviceNameMatch { get; set; } = "";
    public string[] DeviceNameFallbacks { get; set; } = [];
    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 2;
    public int LatencyMs { get; set; } = 20;
    public string ShareMode { get; set; } = "Shared";
    public bool FallBackToShared { get; set; } = true;
    public bool LowLatency { get; set; } = true;
    public bool LowLatencyRequired { get; set; } = false;
    public bool UseEventSync { get; set; } = true;
    public string MmcssThreadPriority { get; set; } = "Pro Audio";
    public bool RawMode { get; set; } = false;
    public string Category { get; set; } = "Media";
    public int MaxVoices { get; set; } = 64;
    public float MasterGain { get; set; } = 1f;
    public bool SoftClip { get; set; } = true;
    public int HeadroomSeconds { get; set; } = 1;
}

public sealed class LibraryOptions
{
    public const string SectionName = "Library";

    public string SoundsDirectory { get; set; } = "sounds";
    public bool Recurse { get; set; } = true;
    public string[] Extensions { get; set; } =
    [
        ".wav", ".wave", ".mp3", ".flac", ".ogg", ".oga", ".opus",
        ".m4a", ".mp4", ".aac", ".aif", ".aiff", ".wma", ".webm"
    ];
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int MaxDecodeSeconds { get; set; } = 600;
    public int ParallelDecodes { get; set; } = 4;
    public string CacheDirectory { get; set; } = ".cache/pcm";
    public bool UseCache { get; set; } = true;
    public bool PruneCache { get; set; } = false;
    public string MetadataFile { get; set; } = "sounds.json";
    public float DefaultGain { get; set; } = 1f;
    public string Sort { get; set; } = "name";
}

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string BindAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 8787;
    public bool AnnounceAddresses { get; set; } = true;
    public bool LogRequests { get; set; } = false;
}

public sealed class UiOptions
{
    public const string SectionName = "Ui";

    public string Title { get; set; } = "YASoundBoard";
    public string DisplayMode { get; set; } = "standalone";
}

public static class ConfigSections
{
    public const string Root = "YASoundBoard";

    public static string Path(string section) => $"{Root}:{section}";
}
