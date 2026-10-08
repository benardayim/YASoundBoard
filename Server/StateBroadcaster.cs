using Microsoft.Extensions.Options;
using YasoundBoard.Audio;
using YasoundBoard.Configuration;
using YasoundBoard.Library;

namespace YasoundBoard.Server;

public sealed class StateBroadcaster : BackgroundService
{
    private readonly Hub _hub;
    private readonly AudioEngine _engine;
    private readonly SoundLibrary _library;
    private readonly UiOptions _ui;
    private readonly ILogger<StateBroadcaster> _log;

    public StateBroadcaster(
        Hub hub,
        AudioEngine engine,
        SoundLibrary library,
        IOptions<UiOptions> ui,
        ILogger<StateBroadcaster> log)
    {
        _hub = hub;
        _engine = engine;
        _library = library;
        _ui = ui.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rate = Math.Clamp(_ui.StateUpdatesPerSecond, 1, 60);
        var interval = TimeSpan.FromMilliseconds(1000.0 / rate);
        var previous = string.Empty;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_hub.ClientCount == 0 || !_engine.Started)
            {
                continue;
            }

            var mixer = _engine.Mixer;
            var active = new List<string>();

            for (var i = 0; i < _library.Count; i++)
            {
                if (mixer.IsSoundActive(i))
                {
                    active.Add(_library.Items[i].Id);
                }
            }

            var signature = string.Join('|', active);
            if (!_ui.ShowLevels && signature == previous)
            {
                continue;
            }

            previous = signature;

            var message = new StateMessage(
                "state",
                MathF.Round(mixer.Peak, 4),
                active.ToArray(),
                MathF.Round(mixer.MasterGain, 3),
                mixer.ActiveVoiceCount);

            try
            {
                await _hub.BroadcastAsync(message, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogDebug("State broadcast failed: {Message}", ex.Message);
            }
        }
    }
}
