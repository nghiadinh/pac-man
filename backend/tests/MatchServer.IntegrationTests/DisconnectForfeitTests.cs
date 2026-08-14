using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace MatchServer.IntegrationTests;

/// <summary>
/// FR-020: a mid-match disconnect ends the match immediately with a forfeit win for whoever
/// remains. There is no reconnect grace period (clarified 2026-08-14), so "immediately" is the
/// assertion, not just "eventually".
/// </summary>
public sealed class DisconnectForfeitTests : IDisposable
{
    private readonly MatchServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Hunter_disconnecting_forfeits_to_the_runner()
    {
        await using var runner = _factory.CreateHubConnection();
        var hunter = _factory.CreateHubConnection();

        var ended = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runner.On<JsonElement>("MatchEnded", o => ended.TrySetResult(o));

        await runner.StartAsync();
        await runner.InvokeAsync<JoinResult>("JoinMatch", (string?)null);
        await hunter.StartAsync();
        await hunter.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        await hunter.DisposeAsync();

        var outcome = await WithTimeout(ended.Task);
        outcome.ShouldBe(winner: "Runner", reason: "Forfeit");
    }

    [Fact]
    public async Task Runner_disconnecting_forfeits_to_the_hunter()
    {
        var runner = _factory.CreateHubConnection();
        await using var hunter = _factory.CreateHubConnection();

        var ended = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        hunter.On<JsonElement>("MatchEnded", o => ended.TrySetResult(o));

        await runner.StartAsync();
        await runner.InvokeAsync<JoinResult>("JoinMatch", (string?)null);
        await hunter.StartAsync();
        await hunter.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        await runner.DisposeAsync();

        var outcome = await WithTimeout(ended.Task);
        outcome.ShouldBe(winner: "Hunter", reason: "Forfeit");
    }

    [Fact]
    public async Task Forfeit_arrives_without_waiting_out_a_grace_period()
    {
        await using var runner = _factory.CreateHubConnection();
        var hunter = _factory.CreateHubConnection();

        var ended = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runner.On<JsonElement>("MatchEnded", o => ended.TrySetResult(o));

        await runner.StartAsync();
        await runner.InvokeAsync<JoinResult>("JoinMatch", (string?)null);
        await hunter.StartAsync();
        await hunter.InvokeAsync<JoinResult>("JoinMatch", (string?)null);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await hunter.DisposeAsync();
        await WithTimeout(ended.Task);
        stopwatch.Stop();

        // Generous relative to "immediate", but far below any plausible reconnect window - this
        // fails loudly if a grace period is ever introduced without updating the spec.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"forfeit took {stopwatch.Elapsed.TotalSeconds:F1}s, which suggests a grace period");
    }

    private static async Task<JsonElement> WithTimeout(Task<JsonElement> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(completed == task, "never received MatchEnded after the opponent disconnected");
        return await task;
    }
}
