using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using YasoundBoard.Audio;
using YasoundBoard.Configuration;
using YasoundBoard.Library;

namespace YasoundBoard.Server;

public sealed class Hub
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly SoundLibrary _library;
    private readonly AudioEngine _engine;
    private readonly UiOptions _ui;
    private readonly AudioOptions _audio;
    private readonly ILogger<Hub> _log;

    public Hub(
        SoundLibrary library,
        AudioEngine engine,
        IOptions<UiOptions> ui,
        IOptions<AudioOptions> audio,
        ILogger<Hub> log)
    {
        _library = library;
        _engine = engine;
        _ui = ui.Value;
        _audio = audio.Value;
        _log = log;
    }

    public int ClientCount => _clients.Count;

    public async Task HandleAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        _clients[id] = socket;
        _log.LogInformation("Phone connected, {Count} client(s) now attached", _clients.Count);

        try
        {
            await SendAsync(socket, BuildHello(), cancellationToken);

            var chunk = new byte[8192];
            var pending = new MemoryStream();

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var received = await socket
                    .ReceiveAsync(new ArraySegment<byte>(chunk), cancellationToken)
                    .ConfigureAwait(false);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken).ConfigureAwait(false);
                    break;
                }

                pending.Write(chunk, 0, received.Count);

                if (!received.EndOfMessage)
                {
                    continue;
                }

                var payload = Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length);
                pending.SetLength(0);

                await DispatchAsync(socket, payload, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The host is shutting down or the phone went away mid-request.
        }
        catch (WebSocketException ex)
        {
            _log.LogDebug("Connection dropped: {Message}", ex.Message);
        }
        finally
        {
            _clients.TryRemove(id, out _);
            _log.LogInformation("Phone disconnected, {Count} client(s) remain", _clients.Count);
        }
    }

    public async Task BroadcastAsync(object message, CancellationToken cancellationToken = default)
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Protocol.Json);

        foreach (var entry in _clients)
        {
            var socket = entry.Value;

            if (socket.State != WebSocketState.Open)
            {
                _clients.TryRemove(entry.Key, out _);
                continue;
            }

            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                _clients.TryRemove(entry.Key, out _);
            }
        }
    }

    private async Task DispatchAsync(WebSocket socket, string payload, CancellationToken cancellationToken)
    {
        ClientMessage? message;

        try
        {
            message = JsonSerializer.Deserialize<ClientMessage>(payload, Protocol.Json);
        }
        catch (JsonException)
        {
            await SendAsync(socket, new ErrorMessage("error", "Malformed message."), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message?.Type is not { Length: > 0 } type)
        {
            return;
        }

        if (!_engine.Started)
        {
            await SendAsync(socket, new ErrorMessage("error", "The audio engine is not running."), cancellationToken).ConfigureAwait(false);
            return;
        }

        var mixer = _engine.Mixer;

        switch (type.ToLowerInvariant())
        {
            case "play":
                if (_library.TryGetIndex(message.Id, out var playIndex))
                {
                    mixer.Post(AudioCommand.Play(playIndex, message.Velocity ?? 1f));
                }

                break;

            case "stop":
                if (_library.TryGetIndex(message.Id, out var stopIndex))
                {
                    mixer.Post(AudioCommand.Stop(stopIndex));
                }

                break;

            case "stopall":
                mixer.Post(AudioCommand.StopAll());
                break;

            case "setmastergain":
                if (message.Value is { } masterGain)
                {
                    mixer.Post(AudioCommand.SetMasterGain(masterGain));
                }

                break;

            case "setsoundgain":
                if (message.Value is { } soundGain && _library.TryGetIndex(message.Id, out var gainIndex))
                {
                    mixer.Post(AudioCommand.SetSoundGain(gainIndex, soundGain));
                }

                break;

            case "ping":
                await SendAsync(socket, new { type = "pong" }, cancellationToken).ConfigureAwait(false);
                break;

            default:
                await SendAsync(socket, new ErrorMessage("error", $"Unknown message type \"{type}\"."), cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private HelloMessage BuildHello()
    {
        var sounds = new List<SoundDto>(_library.Count);

        foreach (var sound in _library.Items)
        {
            if (sound.Hidden)
            {
                continue;
            }

            sounds.Add(new SoundDto(
                sound.Id,
                sound.Name,
                sound.Color,
                MathF.Round(sound.Gain, 3),
                Math.Round(sound.Duration, 3),
                MathF.Round(sound.Peak, 4),
                sound.Order));
        }

        var ui = new UiDto(
            _ui.Title,
            _ui.Theme,
            _ui.AccentColor,
            _ui.Columns,
            _ui.ShowLabels,
            _ui.ShowLevels,
            _ui.ShowStopAll,
            _ui.ShowMasterVolume,
            _ui.ShowConnectionStatus,
            _ui.VibrateOnTap,
            _ui.VibrateMs,
            _ui.FlashOnTap,
            _ui.KeepAwake,
            _ui.PadMinWidthPx,
            _ui.PadGapPx,
            _ui.PadRadiusPx);

        var audio = new AudioDto(
            _engine.DeviceName,
            _engine.ShareMode,
            _engine.LowLatencyActive,
            _engine.ActualLatencyMs,
            _engine.Format.SampleRate,
            _engine.Format.Channels,
            _audio.MaxVoices,
            MathF.Round(_engine.Mixer.MasterGain, 3));

        return new HelloMessage("hello", _ui.Title, sounds.ToArray(), ui, audio);
    }

    private static async Task SendAsync(WebSocket socket, object message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Protocol.Json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }
}
