using YasoundBoard.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AudioOptions>(builder.Configuration.GetSection(AudioOptions.SectionName));
builder.Services.Configure<LibraryOptions>(builder.Configuration.GetSection(LibraryOptions.SectionName));
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));
builder.Services.Configure<UiOptions>(builder.Configuration.GetSection(UiOptions.SectionName));

var app = builder.Build();

app.Run();
