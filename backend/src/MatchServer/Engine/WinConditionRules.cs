using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>
/// Decides whether the match is over, and who won (FR-015, FR-016, FR-017, FR-023).
/// </summary>
/// <remarks>
/// Evaluated LAST in the tick pipeline, on fully settled state, so a win is never declared from a
/// half-applied tick. SC-001 requires every match to reach a definitive outcome; the branches here
/// are exhaustive, which the "every terminal path produces an outcome" test pins down.
/// </remarks>
public static class WinConditionRules
{
    public static void Evaluate(MatchState match)
    {
        if (match.Status == MatchStatus.Ended)
        {
            return; // already decided - first write wins
        }

        // FR-015: an instant win, checked before anything else. This is what makes a full clear
        // on the same tick the clock expires resolve for Pac-Man rather than falling through to
        // the timeout comparison (spec Edge Cases).
        if (match.Map.ClearedFraction >= 1.0)
        {
            // FR-018: the time bonus applies only on a 100% clear.
            ScoringRules.AwardTimeBonus(match);
            match.End(Role.Runner, MatchEndReason.PelletsCleared);
            return;
        }

        // FR-016: likewise instant, and likewise takes precedence over the clock.
        if (match.Pacman is { LivesRemaining: <= 0 })
        {
            match.End(Role.Hunter, MatchEndReason.LivesDepleted);
            return;
        }

        if (match.RemainingMs > 0)
        {
            return;
        }

        EvaluateTimeout(match);
    }

    /// <summary>
    /// FR-017: below 70% cleared the Ghost wins outright; at or above it the scores decide, with
    /// an exact tie going to Pac-Man (FR-023).
    /// </summary>
    private static void EvaluateTimeout(MatchState match)
    {
        if (match.Map.ClearedFraction < BalanceConstants.Match.ClearThresholdPct)
        {
            match.End(Role.Hunter, MatchEndReason.TimeoutClearThresholdMissed);
            return;
        }

        var pacmanScore = match.Pacman?.Score ?? 0;
        var ghostScore = match.Ghost?.Score ?? 0;

        if (pacmanScore >= ghostScore)
        {
            match.End(Role.Runner, MatchEndReason.TimeoutClearThresholdMet);
        }
        else
        {
            match.End(Role.Hunter, MatchEndReason.TimeoutClearThresholdMissed);
        }
    }
}
