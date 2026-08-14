using MatchServer.State;

namespace MatchServer.Hubs;

/// <summary>Per-tick authoritative snapshot sent to one specific client.</summary>
/// <remarks>
/// This type is built PER RECIPIENT, not once per tick, because the Hunter's payload must omit
/// the Runner's position whenever the Runner is not visible (FR-011). Enforcing fog of war by
/// omission at the source - rather than sending everything and asking the client to hide it - is
/// required by Constitution Principle III: data a client should not use but received anyway is an
/// extractable fairness bug, not a UI concern.
/// </remarks>
public sealed record MatchStateDto(
    string MatchId,
    string Status,
    long ElapsedMs,
    long RemainingMs,
    PlayerDto? Pacman,
    PlayerDto? Ghost,
    MapDto Map,
    FrightenedDto? Frightened,
    int ScoreChain,
    OutcomeDto? Outcome);

public sealed record PlayerDto(
    string Role,
    double X,
    double Y,
    string Facing,
    double SpeedMultiplier,
    int LivesRemaining,
    string GhostSubState,
    bool Connected,
    int Score);

public sealed record MapDto(
    string MapId,
    int Width,
    int Height,
    IReadOnlyList<string> Rows,
    IReadOnlyList<PelletDto> Pellets,
    IReadOnlyList<PelletDto> PowerPellets,
    int TotalPelletCount,
    int CollectedPelletCount);

public sealed record PelletDto(int X, int Y, bool Collected);

public sealed record FrightenedDto(long RemainingMs, bool InversionActive);

public sealed record OutcomeDto(
    string Winner,
    string Reason,
    int FinalPacmanScore,
    int FinalGhostScore);

/// <summary>
/// Projects <see cref="MatchState"/> into a recipient-specific <see cref="MatchStateDto"/>.
/// </summary>
public static class MatchStateProjection
{
    /// <summary>
    /// Builds the payload for one recipient.
    /// </summary>
    /// <param name="match">Authoritative match state.</param>
    /// <param name="recipient">Which role is receiving this payload.</param>
    /// <param name="opponentVisible">
    /// Whether the recipient is allowed to see the opposing player's position this tick.
    /// The Runner always receives <c>true</c> (FR-010). For the Hunter this is the output of
    /// VisionRules (FR-011); until US3 lands, callers pass <c>true</c>, which is why fog of war
    /// is absent from the US1/US2 milestones by design.
    /// </param>
    public static MatchStateDto For(MatchState match, Role recipient, bool opponentVisible)
    {
        var pacman = Project(match.Pacman, hide: recipient == Role.Hunter && !opponentVisible);
        var ghost = Project(match.Ghost, hide: false);

        return new MatchStateDto(
            match.MatchId,
            match.Status.ToString(),
            match.ElapsedMs,
            match.RemainingMs,
            pacman,
            ghost,
            ProjectMap(match.Map),
            ProjectFrightened(match),
            match.ScoreChain,
            ProjectOutcome(match.Outcome));
    }

    private static PlayerDto? Project(PlayerState? player, bool hide)
    {
        if (player is null)
        {
            return null;
        }

        if (hide)
        {
            // Omit position and heading entirely. Score, lives, and connection state stay visible:
            // they are on the HUD for both players and leak nothing about location.
            return new PlayerDto(
                player.Role.ToString(),
                double.NaN,
                double.NaN,
                Direction.None.ToString(),
                0,
                player.LivesRemaining,
                player.GhostSubState.ToString(),
                player.Connected,
                player.Score);
        }

        return new PlayerDto(
            player.Role.ToString(),
            player.X,
            player.Y,
            player.Facing.ToString(),
            player.SpeedMultiplier,
            player.LivesRemaining,
            player.GhostSubState.ToString(),
            player.Connected,
            player.Score);
    }

    private static MapDto ProjectMap(MapState map) => new(
        map.MapId,
        map.Width,
        map.Height,
        BuildRows(map),
        map.Pellets.Select(p => new PelletDto(p.X, p.Y, p.Collected)).ToList(),
        map.PowerPellets.Select(p => new PelletDto(p.X, p.Y, p.Collected)).ToList(),
        map.TotalPelletCount,
        map.CollectedPelletCount);

    /// <summary>Wall geometry as one string per row - compact and trivial for the canvas to read.</summary>
    private static List<string> BuildRows(MapState map)
    {
        var rows = new List<string>(map.Height);
        for (var y = 0; y < map.Height; y++)
        {
            var chars = new char[map.Width];
            for (var x = 0; x < map.Width; x++)
            {
                chars[x] = map.Walls[y][x] ? '#' : ' ';
            }

            rows.Add(new string(chars));
        }

        return rows;
    }

    private static FrightenedDto? ProjectFrightened(MatchState match)
    {
        if (match.Frightened is null || !match.IsFrightenedActive)
        {
            return null;
        }

        return new FrightenedDto(
            match.Frightened.ExpiresAtMs - match.ElapsedMs,
            match.IsInversionActive);
    }

    private static OutcomeDto? ProjectOutcome(Outcome? outcome) => outcome is null
        ? null
        : new OutcomeDto(
            outcome.Winner.ToString(),
            outcome.Reason.ToString(),
            outcome.FinalPacmanScore,
            outcome.FinalGhostScore);
}
