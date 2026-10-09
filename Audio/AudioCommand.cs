namespace YASoundBoard.Audio;

public enum AudioCommandKind
{
    Play,
    Stop,
    StopAll,
    SetMasterGain,
    SetSoundGain,
}

public readonly struct AudioCommand
{
    public AudioCommand(AudioCommandKind kind, int index = -1, float value = 0f)
    {
        Kind = kind;
        Index = index;
        Value = value;
    }

    public AudioCommandKind Kind { get; }

    public int Index { get; }

    public float Value { get; }

    public static AudioCommand Play(int index, float velocity = 1f) => new(AudioCommandKind.Play, index, velocity);

    public static AudioCommand Stop(int index) => new(AudioCommandKind.Stop, index);

    public static AudioCommand StopAll() => new(AudioCommandKind.StopAll);

    public static AudioCommand SetMasterGain(float value) => new(AudioCommandKind.SetMasterGain, -1, value);

    public static AudioCommand SetSoundGain(int index, float value) => new(AudioCommandKind.SetSoundGain, index, value);
}
