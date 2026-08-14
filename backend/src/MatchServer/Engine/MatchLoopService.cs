using MatchServer.Generated;
using MatchServer.Hubs;
using MatchServer.State;
using Microsoft.AspNetCore.SignalR;

namespace MatchServer.Engine;

/// <summary>
/// The authoritative simulation. Runs at ~30Hz and is the ONLY place gameplay state advances.
/// </summary>
/// <remarks>
/// Constitution Principle II requires one deterministic outcome per rule, including for events
/// that land on the same tick. That is why <see cref="Advance"/> evaluates the pipeline in a
/// fixed, documented order rather than letting call order emerge incidentally - in particular
/// FR-021, where a normal-state elimination and a Power Pellet pickup on the same tick must always
/// resolve elimination first.
/// </remarks>
public sealed class MatchLoopService(
    MatchManager matches,
    MatchLogger log,
    IHubContext<MatchHub> hub,
    ILogger<MatchLoopService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval =
        TimeSpan.FromMilliseconds(1000.0 / BalanceConstants.Match.TickHz);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "match loop started at {Hz}Hz ({IntervalMs:F1}ms/tick)",
            BalanceConstants.Match.TickHz, TickInterval.TotalMilliseconds);

        using var timer = new PeriodicTimer(TickInterval);
        var tickMs = TickInterval.TotalMilliseconds;

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAllAsync(tickMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad match must not take down the loop for every other match.
                logger.LogError(ex, "match loop tick failed");
            }
        }

        logger.LogInformation("match loop stopped");
    }

    private async Task TickAllAsync(double deltaMs, CancellationToken ct)
    {
        foreach (var handle in matches.ActiveMatches)
        {
            var ended = handle.Locked(match =>
            {
                if (match.Status != MatchStatus.Active)
                {
                    return false;
                }

                Advance(match, deltaMs);
                return match.Status == MatchStatus.Ended;
            });

            await BroadcastAsync(handle, ct);

            if (ended)
            {
                await AnnounceEndAsync(handle, ct);
                matches.Remove(handle.MatchId);
            }
        }
    }

    /// <summary>
    /// Advances one match by one tick.
    /// </summary>
    /// <remarks>
    /// FIXED EVALUATION ORDER - changing it changes documented game rules, so do not reorder
    /// without updating the spec:
    ///   1. clock          - elapsed time moves first so every rule below sees the same instant
    ///   2. speeds         - derive each player's effective multiplier for this tick
    ///   3. movement       - apply validated input intent to positions
    ///   4. collisions     - elimination resolves BEFORE pickups (FR-021)
    ///   5. pickups        - pellet / power pellet collection and the frightened window
    ///   6. timers         - frightened expiry, ghost respawn, anti-camping
    ///   7. win conditions - evaluated last, on fully settled state
    /// Steps 2-6 arrive with their user stories; the ordering seam exists from the start so those
    /// stories slot in without renegotiating determinism.
    /// </remarks>
    private void Advance(MatchState match, double deltaMs)
    {
        // 1. clock
        match.ElapsedMs += (long)Math.Round(deltaMs);

        // 2-6. rule pipeline (US1/US2/US3)

        // 7. win conditions
        EvaluateTimeout(match);
    }

    /// <summary>
    /// The only win condition available before US1 lands: the 180-second clock expiring (FR-014).
    /// The full evaluation - including the 70% threshold and the FR-023 tie-break - is
    /// WinConditionRules, added with User Story 1.
    /// </summary>
    private void EvaluateTimeout(MatchState match)
    {
        if (match.RemainingMs > 0)
        {
            return;
        }

        var cleared = match.Map.ClearedFraction;
        var pacmanScore = match.Pacman?.Score ?? 0;
        var ghostScore = match.Ghost?.Score ?? 0;

        // FR-017: below the threshold the Ghost wins outright. At or above it, scores decide,
        // with an exact tie going to Pac-Man (FR-023).
        var (winner, reason) = cleared >= BalanceConstants.Match.ClearThresholdPct
            ? pacmanScore >= ghostScore
                ? (Role.Runner, MatchEndReason.TimeoutClearThresholdMet)
                : (Role.Hunter, MatchEndReason.TimeoutClearThresholdMissed)
            : (Role.Hunter, MatchEndReason.TimeoutClearThresholdMissed);

        match.End(winner, reason);

        if (match.Outcome is not null)
        {
            log.MatchEnded(match.MatchId, match.Outcome, match.ElapsedMs, cleared);
        }
    }

    /// <summary>
    /// Sends each connection its own projection. Two payloads, not one broadcast, because the
    /// Hunter's view must be able to omit the Runner's position (FR-011).
    /// </summary>
    private async Task BroadcastAsync(MatchHandle handle, CancellationToken ct)
    {
        var payloads = handle.Locked(match =>
        {
            var list = new List<(string ConnectionId, MatchStateDto Dto)>(2);

            if (match.Pacman is { Connected: true } pacman)
            {
                // FR-010: the Runner always sees the whole map.
                list.Add((pacman.ConnectionId, MatchStateProjection.For(match, Role.Runner, true)));
            }

            if (match.Ghost is { Connected: true } ghost)
            {
                // US3 replaces `true` with the VisionRules result (FR-011).
                list.Add((ghost.ConnectionId, MatchStateProjection.For(match, Role.Hunter, true)));
            }

            return list;
        });

        foreach (var (connectionId, dto) in payloads)
        {
            await hub.Clients.Client(connectionId).SendAsync("StateUpdate", dto, ct);
        }
    }

    private async Task AnnounceEndAsync(MatchHandle handle, CancellationToken ct)
    {
        var outcome = handle.Locked(match => match.Outcome);
        if (outcome is null)
        {
            return;
        }

        await hub.Clients.Group(handle.MatchId).SendAsync(
            "MatchEnded",
            new OutcomeDto(
                outcome.Winner.ToString(),
                outcome.Reason.ToString(),
                outcome.FinalPacmanScore,
                outcome.FinalGhostScore),
            ct);
    }
}
