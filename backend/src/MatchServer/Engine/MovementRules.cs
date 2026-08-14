using MatchServer.Generated;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>
/// Grid movement and the speed model (FR-001, FR-006, FR-009, FR-012).
/// </summary>
/// <remarks>
/// Pure functions over state - no hub or networking dependency - so every rule here is unit
/// testable in isolation (research.md §4).
///
/// Movement model: positions are continuous tile coordinates. A player travels along its facing
/// axis and may only change axis at a tile center. Pac-Man's queued turn is applied the moment it
/// reaches the next center (FR-001 pre-buffered cornering, no speed loss); the Ghost turning while
/// off-center takes the multiplicative cornering penalty until it reaches the next center.
/// </remarks>
public static class MovementRules
{
    /// <summary>How close to a tile center counts as "at" it, in tiles.</summary>
    private const double CenterEpsilon = 0.05;

    /// <summary>
    /// The player's effective speed multiplier for this tick, composing every active modifier.
    /// </summary>
    /// <remarks>
    /// Composition is multiplicative, which is why the FR-001 cornering penalty stacks with the
    /// frightened and anti-camping states rather than replacing them (clarified 2026-08-14).
    /// </remarks>
    public static double EffectiveSpeed(PlayerState player, MatchState match)
    {
        if (player.Role == Role.Runner)
        {
            // FR-001: Pac-Man is always at full speed, with no cornering loss under any condition.
            return BalanceConstants.Movement.PacmanBaseSpeed;
        }

        var speed = player.GhostSubState switch
        {
            // FR-009: eyes return to the ghost house fast, and are not slowed by anything else.
            GhostSubState.EyesOnly => BalanceConstants.Movement.EyesSpeed,
            GhostSubState.Respawning => 0,
            // FR-006: frightened overrides the normal base speed.
            GhostSubState.Frightened => BalanceConstants.Movement.GhostSpeedFrightened,
            _ => BalanceConstants.Movement.GhostBaseSpeed,
        };

        if (player.GhostSubState is GhostSubState.EyesOnly or GhostSubState.Respawning)
        {
            return speed;
        }

        // FR-012: anti-camping is an additional 15% reduction (0.95 -> 0.8075 of base pace).
        // Movement only APPLIES the debuff; AntiCampingRules decides when it is active.
        if (AntiCampingRules.IsDebuffActive(match))
        {
            speed *= 1.0 - BalanceConstants.AntiCamping.CampSpeedPenalty;
        }

        // FR-001: multiplicative, and only until the next tile center is reached.
        if (player.CorneringPenaltyActive)
        {
            speed *= BalanceConstants.Movement.GhostCorneringMultiplier;
        }

        return speed;
    }

    /// <summary>
    /// Recomputes each player's reported speed from fully settled state.
    /// </summary>
    /// <remarks>
    /// Called at the END of the tick. Movement runs early in the pipeline, but the sub-state that
    /// determines speed can still change later in the same tick - eating a Power Pellet at the
    /// pickup step frightens the ghost after movement has already recorded 0.95. Without this the
    /// broadcast would briefly show "Frightened" alongside the normal-state speed, which is
    /// visibly wrong on the HUD and confusing to anyone reading a state dump.
    /// </remarks>
    public static void RefreshSpeeds(MatchState match)
    {
        if (match.Pacman is { } pacman)
        {
            pacman.SpeedMultiplier = EffectiveSpeed(pacman, match);
        }

        if (match.Ghost is { } ghost)
        {
            ghost.SpeedMultiplier = EffectiveSpeed(ghost, match);
        }
    }

    /// <summary>Advances both players by one tick.</summary>
    public static void Advance(MatchState match, double deltaMs)
    {
        if (match.Pacman is { } pacman)
        {
            Move(pacman, match, deltaMs);
        }

        if (match.Ghost is { } ghost)
        {
            Move(ghost, match, deltaMs);
        }
    }

    /// <summary>
    /// Moves one player, advancing in steps that stop at each tile center.
    /// </summary>
    /// <remarks>
    /// Stepping center-to-center rather than applying the whole tick's distance at once is what
    /// makes buffered turns work. At 30Hz a player covers a fraction of a tile per tick and will
    /// routinely pass THROUGH a center mid-tick; evaluating the turn only at the start of the tick
    /// would silently drop those inputs and make cornering feel unresponsive. It also prevents
    /// tunnelling through a wall when a single tick's distance exceeds one tile.
    /// </remarks>
    private static void Move(PlayerState player, MatchState match, double deltaMs)
    {
        var speed = EffectiveSpeed(player, match);
        player.SpeedMultiplier = speed;

        if (speed <= 0)
        {
            return;
        }

        var remaining = speed
                        * BalanceConstants.Movement.BaseTilesPerSecond
                        * (deltaMs / 1000.0);

        // Bounded purely as a safety net: each iteration consumes either the rest of the tick's
        // distance or a whole tile, so this cannot legitimately spin.
        for (var guard = 0; remaining > 1e-9 && guard < 64; guard++)
        {
            TryTurn(player, match);

            if (player.Facing == Direction.None)
            {
                return;
            }

            var (dx, dy) = player.Facing.Delta();

            if (IsAtCenter(player) && match.Map.IsWall(TileOf(player.X) + dx, TileOf(player.Y) + dy))
            {
                // Nose against a wall: stop cleanly on the center instead of drifting into it.
                SnapToCenter(player);
                ClearCorneringIfCentered(player);
                return;
            }

            var step = Math.Min(remaining, DistanceToNextCenter(player, dx, dy));

            player.X += dx * step;
            player.Y += dy * step;
            remaining -= step;

            if (IsAtCenter(player))
            {
                SnapToCenter(player);
            }

            ClearCorneringIfCentered(player);
        }
    }

    /// <summary>
    /// Turns the player's stored intent into the direction actually applied this tick.
    /// </summary>
    /// <remarks>
    /// FR-007: during the first 3.0s of a Frightened window the Hunter's directional input is
    /// inverted. This happens HERE, server-side, at the moment the intent is consumed - the client
    /// always sends the player's true intended direction. Inverting on the client would be
    /// unenforceable, since a client could simply decline to do it (Constitution Principle III).
    /// </remarks>
    private static Direction ResolveIntent(PlayerState player, MatchState match)
    {
        var intent = player.DesiredDirection;

        if (player.Role != Role.Hunter)
        {
            return intent;
        }

        // Eyes are server-steered on their way home, so the inversion must not touch them.
        if (player.GhostSubState == GhostSubState.EyesOnly)
        {
            return intent;
        }

        return match.IsInversionActive ? intent.Invert() : intent;
    }

    private static int TileOf(double coordinate) => (int)Math.Round(coordinate);

    private static bool IsAtCenter(PlayerState player) =>
        Math.Abs(player.X - Math.Round(player.X)) < CenterEpsilon &&
        Math.Abs(player.Y - Math.Round(player.Y)) < CenterEpsilon;

    private static void SnapToCenter(PlayerState player)
    {
        player.X = Math.Round(player.X);
        player.Y = Math.Round(player.Y);
    }

    /// <summary>Distance from the current position to the next tile center along the heading.</summary>
    private static double DistanceToNextCenter(PlayerState player, int dx, int dy)
    {
        const double Nudge = 1e-9;

        if (dx > 0)
        {
            return Math.Floor(player.X + Nudge) + 1 - player.X;
        }

        if (dx < 0)
        {
            return player.X - (Math.Ceiling(player.X - Nudge) - 1);
        }

        if (dy > 0)
        {
            return Math.Floor(player.Y + Nudge) + 1 - player.Y;
        }

        return player.Y - (Math.Ceiling(player.Y - Nudge) - 1);
    }

    /// <summary>
    /// Applies the queued turn if it is legal right now.
    /// </summary>
    /// <remarks>
    /// Both roles may only change axis at a tile center - that is what keeps movement on the grid.
    /// The asymmetry is in the cost: Pac-Man's buffered turn is free (FR-001), while the Ghost
    /// pays the cornering penalty whenever it turns while not exactly centered.
    /// </remarks>
    private static void TryTurn(PlayerState player, MatchState match)
    {
        var desired = ResolveIntent(player, match);

        if (desired == Direction.None || desired == player.Facing)
        {
            return;
        }

        var tileX = (int)Math.Round(player.X);
        var tileY = (int)Math.Round(player.Y);
        var (dx, dy) = desired.Delta();

        if (match.Map.IsWall(tileX + dx, tileY + dy))
        {
            return; // cannot turn into a wall; keep the intent queued for the next intersection
        }

        var offCenter = Math.Abs(player.X - tileX) + Math.Abs(player.Y - tileY);

        // A reversal along the current axis needs no intersection - it is always legal.
        var isReversal = desired == player.Facing.Invert();

        if (!isReversal && offCenter > CenterEpsilon)
        {
            return; // wait until the next tile center (Pac-Man's buffered turn lands here)
        }

        if (!isReversal)
        {
            // Snap onto the new axis so the player stays grid-aligned.
            player.X = tileX;
            player.Y = tileY;
        }

        // FR-001: only the Ghost pays for turning, and only when genuinely off-center.
        if (player.Role == Role.Hunter && offCenter > CenterEpsilon)
        {
            player.CorneringPenaltyActive = true;
        }

        player.Facing = desired;
    }

    /// <summary>The cornering penalty lasts only until the next tile center (FR-001).</summary>
    private static void ClearCorneringIfCentered(PlayerState player)
    {
        if (!player.CorneringPenaltyActive)
        {
            return;
        }

        var offCenter = Math.Abs(player.X - Math.Round(player.X))
                        + Math.Abs(player.Y - Math.Round(player.Y));

        if (offCenter <= CenterEpsilon)
        {
            player.CorneringPenaltyActive = false;
        }
    }
}
