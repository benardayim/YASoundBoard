using System.Text.Json;
using Microsoft.Extensions.Options;
using YASoundBoard.Audio;
using YASoundBoard.Configuration;
using YASoundBoard.Library;
using YASoundBoard.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);

builder.Services.AddYASoundBoardOptions(builder.Configuration);

var server = builder.Configuration
    .GetSection(ConfigSections.Path(ServerOptions.SectionName))
    .Get<ServerOptions>() ?? new ServerOptions();

var port = Math.Clamp(server.Port, 1, 65535);
builder.WebHost.UseUrls($"http://{server.BindAddress}:{port}");

builder.Services.AddSingleton<SoundLibrary>();
builder.Services.AddSingleton<AudioEngine>();
builder.Services.AddSingleton<Hub>();
builder.Services.AddHostedService<StateBroadcaster>();

var app = builder.Build();

if (args.Contains("--list-devices", StringComparer.OrdinalIgnoreCase))
{
    app.Logger.LogInformation("Active render devices:");

    foreach (var device in AudioEngine.ListRenderDevices())
    {
        app.Logger.LogInformation("    {Device}", device);
    }

    return;
}

if (server.LogRequests)
{
    app.Use(async (context, next) =>
    {
        app.Logger.LogInformation("{Method} {Path}", context.Request.Method, context.Request.Path);
        await next(context);
    });
}

var library = app.Services.GetRequiredService<SoundLibrary>();
await library.LoadAsync();

var engine = app.Services.GetRequiredService<AudioEngine>();

try
{
    engine.Start();
}
catch (InvalidOperationException ex)
{
    app.Logger.LogError("{Message}", ex.Message);
    Environment.ExitCode = 1;
    return;
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

app.MapGet("/api/state", (IOptions<UiOptions> ui, Hub hub) => Results.Json(new
{
    title = ui.Value.Title,
    sounds = library.Count,
    device = engine.DeviceName,
    shareMode = engine.ShareMode,
    lowLatency = engine.LowLatencyActive,
    latencyMs = engine.ActualLatencyMs,
    running = engine.IsRunning,
    clients = hub.ClientCount,
}, Protocol.Json));

app.MapGet("/manifest.webmanifest", (IOptions<UiOptions> ui) =>
{
    var options = ui.Value;

    var payload = new
    {
        name = options.Title,
        short_name = options.Title,
        start_url = "/",
        scope = "/",
        display = options.DisplayMode,
        background_color = "#0f1014",
        theme_color = "#0f1014",
        icons = new[]
        {
            new { src = "/icon.svg", sizes = "any", type = "image/svg+xml", purpose = "any" },
        },
    };

    return Results.Text(JsonSerializer.Serialize(payload, Protocol.Json), "application/manifest+json");
});

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("This endpoint speaks WebSocket only.");
        return;
    }

    var hub = context.RequestServices.GetRequiredService<Hub>();
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.HandleAsync(socket, context.RequestAborted);
});

if (server.AnnounceAddresses)
{
    app.Lifetime.ApplicationStarted.Register(() => Announce(app, port));
}

app.Run();

static void Announce(WebApplication app, int port)
{
    var addresses = NetworkInfo.LocalAddresses();

    if (addresses.Count == 0)
    {
        app.Logger.LogWarning("No usable network address was found, so the phone will not be able to reach this machine.");
        return;
    }

    app.Logger.LogInformation("Open one of these on the phone:");

    foreach (var entry in addresses.Where(a => NetworkInfo.IsLikelyReachable(a.Address)))
    {
        app.Logger.LogInformation("    http://{Address}:{Port}    ({Interface})", entry.Address, port, entry.Interface);
    }

    foreach (var entry in addresses.Where(a => !NetworkInfo.IsLikelyReachable(a.Address)))
    {
        app.Logger.LogInformation("    http://{Address}:{Port}    ({Interface}, likely unreachable from the phone)", entry.Address, port, entry.Interface);
    }

    app.Logger.LogInformation(
        "If the phone times out, allow this app through Windows Firewall on private networks and check that the router has client isolation turned off.");
}
