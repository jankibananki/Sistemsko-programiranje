using FileSearchWebServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSingleton<CacheService>();
builder.Services.AddSingleton<FileSearchService>();
builder.Services.AddSingleton<LoggerService>();

var app = builder.Build();

app.MapControllers();

app.Run();