using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>
/// Structured per-match logging of authoritative decisions.
/// </summary>
/// <remarks>
/// The constitution's Fair-Play requirements state that win/loss, score, and timer determinations
/// "MUST be reproducible from logged match state for post-match dispute review". This records the
/// decisions that determine an outcome - not every tick's positions, which would be enormous and
/// add nothing a replay of decisions does not already give.
/// </remarks>
public sealed class MatchLogger(ILogger<MatchLogger> logger)
{
    public void MatchCreated(string matchId, string mapId) =>
        logger.LogInformation("match {MatchId} created on map {MapId}", matchId, mapId);

    public void PlayerJoined(string matchId, Role role, string connectionId) =>
        logger.LogInformation(
            "match {MatchId}: {Role} joined ({ConnectionId})", matchId, role, connectionId);

    public void MatchStarted(string matchId) =>
        logger.LogInformation("match {MatchId}: both roles filled, timer started", matchId);

    /// <summary>A room code the server refused. There is no match id yet to attribute it to.</summary>
    public void RoomCodeRejected(string connectionId, string rawValue) =>
        logger.LogWarning(
            "rejected invalid room code {RawValue} from {ConnectionId}", rawValue, connectionId);

    /// <summary>An input the server refused. Rejected, never clamped into gameplay.</summary>
    public void InputRejected(string matchId, string connectionId, string rawValue) =>
        logger.LogWarning(
            "match {MatchId}: rejected invalid input {RawValue} from {ConnectionId}",
            matchId, connectionId, rawValue);

    public void PelletCollected(string matchId, long elapsedMs, int x, int y, bool isPowerPellet) =>
        logger.LogDebug(
            "match {MatchId} @{ElapsedMs}ms: {Kind} collected at ({X},{Y})",
            matchId, elapsedMs, isPowerPellet ? "power pellet" : "pellet", x, y);

    public void PacmanEliminated(string matchId, long elapsedMs, int livesRemaining) =>
        logger.LogInformation(
            "match {MatchId} @{ElapsedMs}ms: Pac-Man eliminated, {LivesRemaining} lives left",
            matchId, elapsedMs, livesRemaining);

    public void GhostCaught(string matchId, long elapsedMs, int chainIndex, int points) =>
        logger.LogInformation(
            "match {MatchId} @{ElapsedMs}ms: ghost caught (chain {ChainIndex}) for {Points} pts",
            matchId, elapsedMs, chainIndex, points);

    public void FrightenedStarted(string matchId, long elapsedMs, bool wasReset) =>
        logger.LogInformation(
            "match {MatchId} @{ElapsedMs}ms: frightened window {Action}",
            matchId, elapsedMs, wasReset ? "reset" : "started");

    public void AntiCampingDebuff(string matchId, long elapsedMs, bool applied) =>
        logger.LogInformation(
            "match {MatchId} @{ElapsedMs}ms: anti-camping debuff {Action}",
            matchId, elapsedMs, applied ? "applied" : "cleared");

    /// <summary>
    /// The decisive record: which side won, why, and the scores it was decided on. This is the
    /// line a dispute review reads first.
    /// </summary>
    public void MatchEnded(string matchId, Outcome outcome, long elapsedMs, double clearedFraction) =>
        logger.LogInformation(
            "match {MatchId} @{ElapsedMs}ms ENDED: {Winner} wins by {Reason} " +
            "(pacman {PacmanScore} / ghost {GhostScore}, {ClearedPct:P1} pellets cleared)",
            matchId, elapsedMs, outcome.Winner, outcome.Reason,
            outcome.FinalPacmanScore, outcome.FinalGhostScore, clearedFraction);

    /// <summary>
    /// Records how long the authoritative loop actually took to apply and broadcast a tick.
    /// </summary>
    /// <remarks>
    /// SC-006 budgets 100ms round-trip from input to effect. Server-side processing is only part
    /// of that (network transit is the rest), so this logs at Warning once the server alone eats
    /// more than half the budget - the point at which the remaining headroom for the network is
    /// no longer credible. The constitution requires this target to be revisited if measured
    /// latency exceeds it, which needs the measurement to exist in the first place.
    /// </remarks>
    public void TickLatency(string matchId, double elapsedMs)
    {
        const double BudgetMs = BalanceConstants.Network.MaxInputLatencyMs;

        if (elapsedMs > BudgetMs / 2)
        {
            logger.LogWarning(
                "match {MatchId}: tick took {ElapsedMs:F1}ms, over half the {BudgetMs}ms " +
                "input-to-effect budget (SC-006)",
                matchId, elapsedMs, BudgetMs);
        }
        else
        {
            logger.LogDebug(
                "match {MatchId}: tick {ElapsedMs:F1}ms", matchId, elapsedMs);
        }
    }

    public void PlayerDisconnected(string matchId, Role role, string connectionId) =>
        logger.LogInformation(
            "match {MatchId}: {Role} disconnected ({ConnectionId}) - forfeiting per FR-020",
            matchId, role, connectionId);
}
