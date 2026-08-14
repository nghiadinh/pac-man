using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>The FR-018 scoring matrix. Scores are server-computed and never client-reported.</summary>
public static class ScoringRules
{
    /// <summary>
    /// Awards the points for one event, folding them into the recipient's running total and
    /// returning the event so the hub can broadcast it (FR-019).
    /// </summary>
    public static ScoreEvent Award(MatchState match, ScoreEventType type)
    {
        var (points, recipient) = type switch
        {
            ScoreEventType.PelletCollected =>
                (BalanceConstants.Scoring.PelletPoints, Role.Runner),
            ScoreEventType.PowerPelletCollected =>
                (BalanceConstants.Scoring.PowerPelletPoints, Role.Runner),
            ScoreEventType.GhostCaught =>
                (ChainPoints(match.ScoreChain), Role.Runner),
            ScoreEventType.PacmanEliminated =>
                (BalanceConstants.Scoring.PacmanEliminatedPoints, Role.Hunter),
            ScoreEventType.TimeBonus =>
                (TimeBonusPoints(match), Role.Runner),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unhandled score event"),
        };

        Credit(match, recipient, points);
        return new ScoreEvent(type, points, recipient);
    }

    /// <summary>
    /// FR-018: +5 per whole second remaining, awarded only when Pac-Man clears 100% of pellets.
    /// The caller is responsible for that precondition (see <see cref="WinConditionRules"/>).
    /// </summary>
    public static ScoreEvent AwardTimeBonus(MatchState match) =>
        Award(match, ScoreEventType.TimeBonus);

    /// <summary>
    /// FR-009: 200 / 400 / 800 / 1600 for consecutive catches in one unbroken chain.
    /// Clamped at the top value - a fifth catch in a single window (only reachable with
    /// overlapping frightened windows) must not run off the end of the table.
    /// </summary>
    public static int ChainPoints(int chainIndex)
    {
        var chain = BalanceConstants.Scoring.GhostCatchChain;
        var index = Math.Clamp(chainIndex, 0, chain.Length - 1);
        return chain[index];
    }

    private static int TimeBonusPoints(MatchState match) =>
        (int)(match.RemainingMs / 1000) * BalanceConstants.Scoring.TimeBonusPerSecond;

    private static void Credit(MatchState match, Role recipient, int points)
    {
        var player = recipient == Role.Runner ? match.Pacman : match.Ghost;
        if (player is not null)
        {
            player.Score += points;
        }
    }
}
