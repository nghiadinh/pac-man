using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>Result of one tick's pellet collection.</summary>
public sealed record PelletResult
{
    public IReadOnlyList<ScoreEvent> ScoreEvents { get; init; } = [];

    public bool PowerPelletEaten { get; init; }

    public static readonly PelletResult None = new();
}

/// <summary>
/// Pellet and Power Pellet collection (FR-018 scoring, FR-005 frightened trigger) and the
/// clear-percentage tracking the FR-015/FR-017 thresholds are measured against.
/// </summary>
public static class PelletRules
{
    /// <summary>
    /// Collects whatever Pac-Man is standing on this tick.
    /// </summary>
    /// <remarks>
    /// Runs AFTER collision resolution. That ordering is FR-021: if a normal-state elimination and
    /// a Power Pellet pickup land on the same tick, the life is already gone by the time the
    /// pellet is consumed, and the frightened window that pickup creates begins from Pac-Man's
    /// respawned position rather than retroactively saving the life.
    /// </remarks>
    public static PelletResult Collect(MatchState match)
    {
        if (match.Status != MatchStatus.Active || match.Pacman is not { } pacman)
        {
            return PelletResult.None;
        }

        var tileX = (int)Math.Round(pacman.X);
        var tileY = (int)Math.Round(pacman.Y);

        var events = new List<ScoreEvent>();
        var powerEaten = false;

        foreach (var pellet in match.Map.Pellets)
        {
            if (!pellet.Collected && pellet.X == tileX && pellet.Y == tileY)
            {
                pellet.Collected = true;
                events.Add(ScoringRules.Award(match, ScoreEventType.PelletCollected));
                break; // at most one pellet per tile
            }
        }

        foreach (var power in match.Map.PowerPellets)
        {
            if (power.Collected || power.X != tileX || power.Y != tileY)
            {
                continue;
            }

            power.Collected = true;
            power.CampTimerMs = 0;
            power.CampDebuffActive = false;
            events.Add(ScoringRules.Award(match, ScoreEventType.PowerPelletCollected));
            powerEaten = true;
            break;
        }

        return events.Count == 0
            ? PelletResult.None
            : new PelletResult { ScoreEvents = events, PowerPelletEaten = powerEaten };
    }

    /// <summary>
    /// FR-005: starts a fresh 8.0s frightened window, or resets an active one. Never stacks.
    /// </summary>
    public static bool StartOrResetFrightened(MatchState match)
    {
        var wasActive = match.IsFrightenedActive;

        if (match.Frightened is null)
        {
            match.Frightened = new FrightenedState { StartedAtMs = match.ElapsedMs };
        }
        else
        {
            match.Frightened.Reset(match.ElapsedMs);
        }

        // A new window starts a new catch chain (FR-009).
        match.ScoreChain = 0;

        if (match.Ghost is { } ghost &&
            ghost.GhostSubState is GhostSubState.Normal)
        {
            ghost.GhostSubState = GhostSubState.Frightened;
        }

        return wasActive;
    }

    /// <summary>Whether the map is fully cleared (FR-015).</summary>
    public static bool IsFullyCleared(MatchState match) => match.Map.ClearedFraction >= 1.0;

    /// <summary>Whether Pac-Man met the FR-017 timeout threshold.</summary>
    public static bool MeetsClearThreshold(MatchState match) =>
        match.Map.ClearedFraction >= BalanceConstants.Match.ClearThresholdPct;
}
