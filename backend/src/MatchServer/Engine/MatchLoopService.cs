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
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var (ended, events) = handle.Locked(match =>
            {
                if (match.Status != MatchStatus.Active)
                {
                    return (false, (IReadOnlyList<ScoreEvent>)Array.Empty<ScoreEvent>());
                }

                var produced = Advance(match, deltaMs);
                return (match.Status == MatchStatus.Ended, (IReadOnlyList<ScoreEvent>)produced);
            });

            // FR-019: both players see every scoring action, within the SC-005 one-second budget.
            foreach (var scoreEvent in events)
            {
                await hub.Clients.Group(handle.MatchId).SendAsync(
                    "ScoreEvent",
                    new
                    {
                        eventType = scoreEvent.Type.ToString(),
                        points = scoreEvent.Points,
                        recipient = scoreEvent.Recipient.ToString(),
                    },
                    ct);
            }

            await BroadcastAsync(handle, ct);
            await SendSonarIfDueAsync(handle, ct);

            // SC-006: the server's own share of the input-to-effect budget.
            stopwatch.Stop();
            log.TickLatency(handle.MatchId, stopwatch.Elapsed.TotalMilliseconds);

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
    private List<ScoreEvent> Advance(MatchState match, double deltaMs)
    {
        var events = new List<ScoreEvent>();

        // 1. clock
        match.ElapsedMs += (long)Math.Round(deltaMs);

        // 2. ghost sub-state - resolved before movement so this tick moves at the right speed and
        //    eyes are already steered toward home
        GhostStateMachine.Advance(match);

        // 3. speeds and movement
        MovementRules.Advance(match, deltaMs);

        // 4. collisions - BEFORE pickups, which is the FR-021 rule
        var collision = CollisionRules.Resolve(match);
        events.AddRange(collision.ScoreEvents);

        if (collision.PacmanEliminated)
        {
            log.PacmanEliminated(match.MatchId, match.ElapsedMs, match.Pacman?.LivesRemaining ?? 0);
        }

        // 5. pickups
        var pellets = PelletRules.Collect(match);
        events.AddRange(pellets.ScoreEvents);

        if (pellets.PowerPelletEaten)
        {
            var wasReset = PelletRules.StartOrResetFrightened(match);
            log.FrightenedStarted(match.MatchId, match.ElapsedMs, wasReset);
        }

        // FR-021: the respawn is applied only now, so a Power Pellet sharing the elimination tile
        // was still consumed above.
        if (collision.PacmanEliminated)
        {
            CollisionRules.RespawnRunner(match);
        }

        // 6. timers - the post-elimination respawn delay (FR-003); the frightened window and
        //    ghost-house lockout are handled by GhostStateMachine in step 2
        ExpireGhostRespawn(match);

        var campedBefore = AntiCampingRules.IsDebuffActive(match);
        AntiCampingRules.Advance(match, deltaMs);
        var campedAfter = AntiCampingRules.IsDebuffActive(match);

        if (campedBefore != campedAfter)
        {
            log.AntiCampingDebuff(match.MatchId, match.ElapsedMs, campedAfter);
        }

        // 7. reported speeds, recomputed from settled state so the broadcast cannot show a
        //    sub-state and a speed that disagree
        MovementRules.RefreshSpeeds(match);

        // 8. win conditions, on fully settled state
        WinConditionRules.Evaluate(match);

        if (match.Status == MatchStatus.Ended && match.Outcome is not null)
        {
            log.MatchEnded(
                match.MatchId, match.Outcome, match.ElapsedMs, match.Map.ClearedFraction);
        }

        return events;
    }

    /// <summary>
    /// FR-003: returns the Ghost to play once its 5-second respawn delay has elapsed.
    /// The eyes-only leg of this (FR-009) arrives with User Story 2.
    /// </summary>
    private static void ExpireGhostRespawn(MatchState match)
    {
        if (match.Ghost is not { GhostSubState: GhostSubState.Respawning } ghost)
        {
            return;
        }

        if (ghost.RespawnReadyAtMs is { } readyAt && match.ElapsedMs >= readyAt)
        {
            ghost.GhostSubState = GhostSubState.Normal;
            ghost.RespawnReadyAtMs = null;
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
                // FR-011: the Runner's position is omitted from this payload entirely whenever
                // vision says it is not visible - withheld at the source, not hidden by the client.
                var visible = VisionRules.IsRunnerVisibleToHunter(match);
                list.Add((ghost.ConnectionId, MatchStateProjection.For(match, Role.Hunter, visible)));
            }

            return list;
        });

        foreach (var (connectionId, dto) in payloads)
        {
            await hub.Clients.Client(connectionId).SendAsync("StateUpdate", dto, ct);
        }
    }

    /// <summary>
    /// FR-011: pulses the Hunter every 4.0s while the Runner is out of sight.
    /// </summary>
    /// <remarks>
    /// Sent to the Hunter's connection only, and carries a map-relative quadrant and nothing else -
    /// no coordinates, no bearing. Suppressed while the Runner IS visible, since the Hunter can
    /// already see exactly where he is.
    /// </remarks>
    private async Task SendSonarIfDueAsync(MatchHandle handle, CancellationToken ct)
    {
        var pulse = handle.Locked(match =>
        {
            if (match.Status != MatchStatus.Active || match.Ghost is not { Connected: true } ghost)
            {
                return ((string ConnectionId, Quadrant Quadrant)?)null;
            }

            if (VisionRules.IsRunnerVisibleToHunter(match))
            {
                return null;
            }

            if (!SonarRules.IsPulseDue(match, _lastSonarAtMs.GetValueOrDefault(match.MatchId)))
            {
                return null;
            }

            _lastSonarAtMs[match.MatchId] = match.ElapsedMs;
            return (ghost.ConnectionId, SonarRules.QuadrantOf(match));
        });

        if (pulse is { } value)
        {
            await hub.Clients.Client(value.ConnectionId)
                .SendAsync("SonarPulse", value.Quadrant.ToString(), ct);
        }
    }

    /// <summary>Last sonar time per match. Keyed by match id so concurrent matches never share it.</summary>
    private readonly Dictionary<string, long?> _lastSonarAtMs = [];

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
