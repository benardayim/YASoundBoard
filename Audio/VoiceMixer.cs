using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.Wave;
using YASoundBoard.Configuration;
using YASoundBoard.Library;

namespace YASoundBoard.Audio;

public sealed class VoiceMixer : IWaveProvider
{
    private readonly AudioOptions _options;
    private readonly SoundLibrary _library;
    private readonly ConcurrentQueue<AudioCommand> _commands = new();
    private readonly Voice[] _voices;
    private readonly float[] _mix;
    private readonly int[] _activeCounts;
    private readonly int _channels;
    private readonly int _maxFrames;

    private float _masterGain;
    private int _peakBits;

    public VoiceMixer(AudioOptions options, SoundLibrary library)
    {
        _options = options;
        _library = library;
        _channels = Math.Max(1, options.Channels);

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(options.SampleRate, _channels);

        var headroom = Math.Max(1, options.HeadroomSeconds);
        var latency = Math.Max(0, options.LatencyMs) / 1000.0;
        _maxFrames = (int)(options.SampleRate * Math.Max(headroom, latency + 1));

        _mix = new float[_maxFrames * _channels];
        _voices = new Voice[Math.Max(1, options.MaxVoices)];
        _activeCounts = new int[Math.Max(1, library.Count)];
        _masterGain = options.MasterGain;
    }

    public WaveFormat WaveFormat { get; }

    public float Peak => BitConverter.Int32BitsToSingle(Interlocked.CompareExchange(ref _peakBits, 0, 0));

    public float MasterGain => _masterGain;

    public void Post(AudioCommand command) => _commands.Enqueue(command);

    public int ActiveVoiceCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].Active)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool IsSoundActive(int index)
        => (uint)index < (uint)_activeCounts.Length && Volatile.Read(ref _activeCounts[index]) > 0;

    public int Read(Span<byte> buffer)
    {
        Drain();

        var frames = buffer.Length / (sizeof(float) * _channels);
        if (frames > _maxFrames)
        {
            frames = _maxFrames;
        }

        var sampleCount = frames * _channels;
        var mix = _mix.AsSpan(0, sampleCount);
        mix.Clear();

        for (var i = 0; i < _voices.Length; i++)
        {
            ref var voice = ref _voices[i];
            if (!voice.Active)
            {
                continue;
            }

            var source = voice.Samples;
            if (source is null || source.Length == 0)
            {
                Finish(ref voice);
                continue;
            }

            var take = Math.Min(source.Length - voice.Position, sampleCount);
            if (take <= 0)
            {
                Finish(ref voice);
                continue;
            }

            var offset = voice.Position;
            var gain = voice.Gain;

            for (var s = 0; s < take; s++)
            {
                mix[s] += source[offset + s] * gain;
            }

            voice.Position += take;

            if (voice.Position >= source.Length)
            {
                if (voice.Loop)
                {
                    voice.Position = 0;
                }
                else
                {
                    Finish(ref voice);
                }
            }
        }

        var master = _masterGain;
        var peak = 0f;

        for (var i = 0; i < sampleCount; i++)
        {
            var value = mix[i] * master;
            mix[i] = value;
            var magnitude = value < 0f ? -value : value;
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        if (peak > 1f)
        {
            if (_options.SoftClip)
            {
                for (var i = 0; i < sampleCount; i++)
                {
                    mix[i] = (float)Math.Tanh(mix[i]);
                }
            }
            else
            {
                for (var i = 0; i < sampleCount; i++)
                {
                    var value = mix[i];
                    mix[i] = value > 1f ? 1f : value < -1f ? -1f : value;
                }
            }

            peak = 1f;
        }

        Interlocked.Exchange(ref _peakBits, BitConverter.SingleToInt32Bits(peak));

        var written = sampleCount * sizeof(float);
        MemoryMarshal.AsBytes(mix).CopyTo(buffer);

        if (written < buffer.Length)
        {
            buffer[written..].Clear();
        }

        return buffer.Length;
    }

    private void Drain()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command.Kind)
            {
                case AudioCommandKind.Play:
                    Start(command.Index, command.Value);
                    break;

                case AudioCommandKind.Stop:
                    Stop(command.Index);
                    break;

                case AudioCommandKind.StopAll:
                    for (var i = 0; i < _voices.Length; i++)
                    {
                        if (_voices[i].Active)
                        {
                            Finish(ref _voices[i]);
                        }
                    }

                    break;

                case AudioCommandKind.SetMasterGain:
                    _masterGain = command.Value < 0f ? 0f : command.Value;
                    break;

                case AudioCommandKind.SetSoundGain:
                    if ((uint)command.Index < (uint)_library.Count)
                    {
                        _library.Items[command.Index].Gain = command.Value < 0f ? 0f : command.Value;
                    }

                    break;
            }
        }
    }

    private void Start(int index, float velocity)
    {
        if ((uint)index >= (uint)_library.Count)
        {
            return;
        }

        var sound = _library.Items[index];
        if (sound.Samples.Length == 0)
        {
            return;
        }

        var slot = FindSlot();
        ref var voice = ref _voices[slot];

        if (voice.Active)
        {
            Deactivate(voice.SoundIndex);
        }

        voice.Samples = sound.Samples;
        voice.Position = 0;
        voice.Gain = sound.Gain * (velocity > 0f ? velocity : 1f);
        voice.Loop = false;
        voice.SoundIndex = index;
        voice.Active = true;

        Activate(index);
    }

    private void Stop(int index)
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            if (_voices[i].Active && _voices[i].SoundIndex == index)
            {
                Finish(ref _voices[i]);
            }
        }
    }

    private int FindSlot()
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            if (!_voices[i].Active)
            {
                return i;
            }
        }

        var chosen = 0;
        var shortest = int.MaxValue;

        for (var i = 0; i < _voices.Length; i++)
        {
            var remaining = (_voices[i].Samples?.Length ?? 0) - _voices[i].Position;
            if (remaining < shortest)
            {
                shortest = remaining;
                chosen = i;
            }
        }

        return chosen;
    }

    private void Finish(ref Voice voice)
    {
        if (!voice.Active)
        {
            return;
        }

        voice.Active = false;
        voice.Samples = null;
        Deactivate(voice.SoundIndex);
    }

    private void Activate(int index)
    {
        if ((uint)index < (uint)_activeCounts.Length)
        {
            Interlocked.Increment(ref _activeCounts[index]);
        }
    }

    private void Deactivate(int index)
    {
        if ((uint)index < (uint)_activeCounts.Length)
        {
            Interlocked.Decrement(ref _activeCounts[index]);
        }
    }

    private struct Voice
    {
        public bool Active;
        public float[]? Samples;
        public int Position;
        public float Gain;
        public bool Loop;
        public int SoundIndex;
    }
}
