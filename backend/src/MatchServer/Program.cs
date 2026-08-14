using MatchServer.Engine;
using MatchServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

// One shared registry of live matches, plus the loop that advances them. Both are singletons:
// a match is server-owned state with no per-request scope (Constitution Principle III).
builder.Services.AddSingleton<MatchManager>();
builder.Services.AddSingleton<MatchLogger>();
builder.Services.AddHostedService<MatchLoopService>();

const string DevCorsPolicy = "vite-dev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        // SignalR negotiates with credentials, so the origin list must stay explicit -
        // AllowAnyOrigin is incompatible with AllowCredentials.
        .AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseCors(DevCorsPolicy);
}

// Playwright's webServer waits on this before starting the e2e run.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapHub<MatchHub>("/hubs/match");

app.Run();

/// <summary>Exposed so WebApplicationFactory can boot this app in integration tests.</summary>
public partial class Program;
