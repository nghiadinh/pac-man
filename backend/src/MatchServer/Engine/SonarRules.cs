using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>
/// The sonar pulse (FR-011): every 4.0s while the Runner is out of sight, the Hunter is told which
/// map quadrant the Runner is in - and nothing more.
/// </summary>
public static class SonarRules
{
    /// <summary>
    /// The Runner's quadrant, resolved against the MAP's midlines.
    /// </summary>
    /// <remarks>
    /// Map-relative, never Hunter-relative (clarified 2026-08-14). A Hunter-relative quadrant
    /// would amount to a bearing-to-target arrow, which is far stronger information than
    /// "approximate quadrant" implies and would erode the vision disadvantage this rule creates.
    /// A map quadrant leaves the Runner anywhere within a quarter of the board.
    /// </remarks>
    public static Quadrant QuadrantOf(MatchState match)
    {
        var pacman = match.Pacman;
        var midX = match.Map.Width / 2.0;
        var midY = match.Map.Height / 2.0;

        var x = pacman?.X ?? midX;
        var y = pacman?.Y ?? midY;

        var north = y < midY;
        var east = x >= midX;

        return north
            ? east ? Quadrant.NE : Quadrant.NW
            : east ? Quadrant.SE : Quadrant.SW;
    }

    /// <summary>
    /// Whether a pulse is due. The first one fires immediately, so a Hunter who starts the match
    /// with no line of sight is not left with nothing at all for four seconds.
    /// </summary>
    public static bool IsPulseDue(MatchState match, long? lastPulseAtMs) =>
        lastPulseAtMs is not { } last ||
        match.ElapsedMs - last >= BalanceConstants.Vision.SonarIntervalMs;
}
