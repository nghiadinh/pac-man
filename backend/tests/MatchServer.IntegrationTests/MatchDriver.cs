using System.Text.Json;
using MatchServer.Engine;
using MatchServer.State;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace MatchServer.IntegrationTests;

/// <summary>
/// Joins two clients and drives a match to a specific outcome.
/// </summary>
/// <remarks>
/// Some outcomes cannot be reached by playing through the hub in a test - clearing 256 pellets or
/// waiting out a 3-minute clock would make the suite unusable. So these tests reach into the
/// server's own MatchState to set up the board position, then let the REAL tick pipeline and hub
/// decide and announce the result. What is asserted is still the server's decision travelling the
/// real wire path; only the setup is shortcut.
/// </remarks>
public sealed class MatchDriver : IAsyncDisposable
{
    private readonly MatchServerFactory _factory;

    private MatchDriver(MatchServerFactory factory, HubConnection runner, HubConnection hunter)
    {
        _factory = factory;
        Runner = runner;
        Hunter = hunter;
    }

    public HubConnection Runner { get; }

    public HubConnection Hunter { get; }

    public TaskCompletionSource<JsonElement> RunnerMatchEnded { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<JsonElement> HunterMatchEnded { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task<MatchDriver> StartAsync(MatchServerFactory factory)
    {
        var runner = factory.CreateHubConnection();
        var hunter = factory.CreateHubConnection();

        var driver = new MatchDriver(factory, runner, hunter);

        runner.On<JsonElement>("MatchEnded", o => driver.RunnerMatchEnded.TrySetResult(o));
        hunter.On<JsonElement>("MatchEnded", o => driver.HunterMatchEnded.TrySetResult(o));

        await runner.StartAsync();
        var runnerJoin = await runner.InvokeAsync<JoinResult>("JoinMatch", (string?)null);
        Assert.Equal("Runner", runnerJoin.Role);

        await hunter.StartAsync();
        var hunterJoin = await hunter.InvokeAsync<JoinResult>("JoinMatch", (string?)null);
        Assert.Equal("Hunter", hunterJoin.Role);

        return driver;
    }

    /// <summary>Mutates the live match under its own lock, exactly as the tick loop would.</summary>
    public void Arrange(Action<MatchState> setup)
    {
        var manager = _factory.Services.GetRequiredService<MatchManager>();
        var handle = manager.ActiveMatches.Single();
        handle.Locked(setup);
    }

    /// <summary>Waits for the server to announce the end of the match.</summary>
    public async Task<JsonElement> WaitForEndAsync(TimeSpan? timeout = null)
    {
        var completed = await Task.WhenAny(
            RunnerMatchEnded.Task,
            Task.Delay(timeout ?? TimeSpan.FromSeconds(10)));

        Assert.True(
            completed == RunnerMatchEnded.Task,
            "server never announced MatchEnded within the timeout");

        return await RunnerMatchEnded.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await Runner.DisposeAsync();
        await Hunter.DisposeAsync();
    }
}

public static class MatchEndedAssertions
{
    public static void ShouldBe(this JsonElement outcome, string winner, string reason)
    {
        Assert.Equal(winner, outcome.GetProperty("winner").GetString());
        Assert.Equal(reason, outcome.GetProperty("reason").GetString());
    }
}
