namespace YASoundBoard.Library;

public sealed class Sound
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string SourcePath { get; init; }
    public required float[] Samples { get; init; }
    public required int Frames { get; init; }
    public required float Peak { get; init; }
    public required double Duration { get; init; }

    public float Gain { get; set; } = 1f;
    public bool Hidden { get; set; }
    public string? Color { get; set; }
    public int Order { get; set; }
}

public sealed class SoundOverride
{
    public string? Name { get; set; }
    public string? Color { get; set; }
    public float? Gain { get; set; }
    public bool? Hidden { get; set; }
    public int? Order { get; set; }
}
