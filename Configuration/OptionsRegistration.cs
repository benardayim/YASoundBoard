using Microsoft.Extensions.Options;

namespace YasoundBoard.Configuration;

public static class OptionsRegistration
{
    public static IServiceCollection AddYasoundBoardOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AudioOptions>()
            .Bind(configuration.GetSection(ConfigSections.Path(AudioOptions.SectionName)))
            .Validate(o => o.SampleRate is >= 8000 and <= 384000, "Audio.SampleRate must be between 8000 and 384000.")
            .Validate(o => o.Channels is >= 1 and <= 8, "Audio.Channels must be between 1 and 8.")
            .Validate(o => o.LatencyMs is >= 1 and <= 5000, "Audio.LatencyMs must be between 1 and 5000.")
            .Validate(o => o.MaxVoices is >= 1 and <= 1024, "Audio.MaxVoices must be between 1 and 1024.")
            .Validate(o => o.HeadroomSeconds is >= 1 and <= 60, "Audio.HeadroomSeconds must be between 1 and 60.")
            .Validate(o => o.MasterGain is >= 0f and <= 8f, "Audio.MasterGain must be between 0 and 8.")
            .ValidateOnStart();

        services
            .AddOptions<LibraryOptions>()
            .Bind(configuration.GetSection(ConfigSections.Path(LibraryOptions.SectionName)))
            .Validate(o => o.ParallelDecodes is >= 1 and <= 64, "Library.ParallelDecodes must be between 1 and 64.")
            .Validate(o => o.MaxDecodeSeconds is >= 0 and <= 86400, "Library.MaxDecodeSeconds must be between 0 and 86400.")
            .Validate(o => o.Extensions.Length > 0, "Library.Extensions must list at least one file extension.")
            .ValidateOnStart();

        services
            .AddOptions<ServerOptions>()
            .Bind(configuration.GetSection(ConfigSections.Path(ServerOptions.SectionName)))
            .Validate(o => o.Port is >= 1 and <= 65535, "Server.Port must be between 1 and 65535.")
            .ValidateOnStart();

        services
            .AddOptions<UiOptions>()
            .Bind(configuration.GetSection(ConfigSections.Path(UiOptions.SectionName)))
            .Validate(o => o.StateUpdatesPerSecond is >= 1 and <= 60, "Ui.StateUpdatesPerSecond must be between 1 and 60.")
            .Validate(o => o.Columns is >= 0 and <= 12, "Ui.Columns must be between 0 and 12, where 0 means automatic.")
            .Validate(o => o.PadMinWidthPx is >= 24f and <= 400f, "Ui.PadMinWidthPx must be between 24 and 400.")
            .Validate(o => o.VibrateMs is >= 0 and <= 1000, "Ui.VibrateMs must be between 0 and 1000.")
            .ValidateOnStart();

        return services;
    }
}
