using System.Text.Json;
using System.Text.Json.Serialization;

namespace YasoundBoard.Server;

public sealed class ClientMessage
{
    public string? Type { get; set; }

    public string? Id { get; set; }

    public float? Value { get; set; }

    public float? Velocity { get; set; }
}

public sealed record SoundDto(
    string Id,
    string Name,
    string? Color,
    float Gain,
    double Duration,
    float Peak,
    int Order);

public sealed record AudioDto(
    string Device,
    string ShareMode,
    bool LowLatency,
    double LatencyMs,
    int SampleRate,
    int Channels,
    int MaxVoices,
    float MasterGain);

public sealed record HelloMessage(
    string Type,
    string Title,
    SoundDto[] Sounds,
    AudioDto Audio);

public sealed record StateMessage(
    string Type,
    string[] Active,
    float MasterGain);

public sealed record ErrorMessage(string Type, string Message);

public static class Protocol
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
