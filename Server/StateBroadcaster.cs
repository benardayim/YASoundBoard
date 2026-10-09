using YasoundBoard.Audio;
using YasoundBoard.Library;

namespace YasoundBoard.Server;

public sealed class StateBroadcaster : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly Hub _hub;
    private readonly AudioEngine _engine;
    private readonly SoundLibrary _library;
    private readonly ILogger<StateBroadcaster> _log;

    public StateBroadcaster(
        Hub hub,
        AudioEngine engine,
        SoundLibrary library,
        ILogger<StateBroadcaster> log)
    {
        _hub = hub;
        _engine = engine;
        _library = library;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var previous = string.Empty;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
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

            var gain = MathF.Round(mixer.MasterGain, 3);
            var signature = string.Join('|', active) + "#" + gain;

            if (signature == previous)
            {
                continue;
            }

            previous = signature;

            var message = new StateMessage("state", active.ToArray(), gain);

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
