using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>
/// FR-012 anti-camping: the Hunter cannot profitably park on an uncollected Power Pellet.
/// </summary>
/// <remarks>
/// The rule exists to kill a degenerate strategy - guarding Pac-Man's only counter-play tool
/// instead of hunting. The "while Pac-Man is not visible" qualifier (clarified 2026-08-14) is what
/// keeps it from punishing a legitimate chase that happens to end near a Power Pellet: if the
/// target is right there, the Hunter is hunting, not camping.
/// </remarks>
public static class AntiCampingRules
{
    /// <summary>Advances every camp-zone timer for this tick and applies or clears the debuff.</summary>
    public static void Advance(MatchState match, double deltaMs)
    {
        if (match.Ghost is not { } ghost)
        {
            return;
        }

        // Eyes are server-steered home and cannot choose their route, so penalising them would
        // punish the Hunter for something it does not control.
        var exempt = ghost.GhostSubState is GhostSubState.EyesOnly or GhostSubState.Respawning;
        var runnerVisible = VisionRules.IsRunnerVisibleToHunter(match);

        foreach (var pellet in match.Map.PowerPellets)
        {
            if (pellet.Collected)
            {
                // The trigger requires an UNCOLLECTED pellet, so eating it releases the zone.
                pellet.CampTimerMs = 0;
                pellet.CampDebuffActive = false;
                continue;
            }

            var inZone = !exempt && IsInZone(ghost, pellet);

            if (!inZone || runnerVisible)
            {
                pellet.CampTimerMs = 0;
                pellet.CampDebuffActive = false;
                continue;
            }

            pellet.CampTimerMs += deltaMs;
            pellet.CampDebuffActive = pellet.CampTimerMs > BalanceConstants.AntiCamping.CampTriggerMs;
        }
    }

    /// <summary>Whether any zone is currently penalising the Hunter.</summary>
    public static bool IsDebuffActive(MatchState match) =>
        match.Map.PowerPellets.Any(p => p.CampDebuffActive);

    private static bool IsInZone(PlayerState ghost, PowerPellet pellet)
    {
        var dx = ghost.X - pellet.X;
        var dy = ghost.Y - pellet.Y;
        return Math.Sqrt(dx * dx + dy * dy) <= BalanceConstants.AntiCamping.CampRadiusTiles;
    }
}
