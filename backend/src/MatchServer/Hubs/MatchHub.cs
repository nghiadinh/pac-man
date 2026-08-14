using MatchServer.Engine;
using MatchServer.State;
using Microsoft.AspNetCore.SignalR;

namespace MatchServer.Hubs;

/// <summary>
/// The one external interface of this feature (contracts/match-room-protocol.md).
/// </summary>
/// <remarks>
/// Clients invoke <see cref="JoinMatch"/> and <see cref="SendInput"/> and receive StateUpdate,
/// SonarPulse, ScoreEvent, and MatchEnded. They never send position, speed, or score: the hub
/// accepts input INTENT only, and every gameplay decision is made in the tick loop
/// (Constitution Principle III).
/// </remarks>
public sealed class MatchHub(MatchManager matches, MatchLogger log) : Hub
{
    /// <summary>
    /// Joins a match, or creates one. First joiner is the Runner, second the Hunter; the
    /// 180-second timer starts when the second player arrives.
    /// </summary>
    /// <param name="roomCode">
    /// Omit to be paired with whoever is waiting. Supply a code to play with a specific person:
    /// whichever of the two arrives first opens the room, and the second joins it. Codes are
    /// case-insensitive.
    /// </param>
    public async Task<JoinResultDto> JoinMatch(string? roomCode = null)
    {
        var outcome = matches.JoinOrCreate(Context.ConnectionId, roomCode);

        switch (outcome.Status)
        {
            case JoinStatus.InvalidRoomCode:
                log.RoomCodeRejected(Context.ConnectionId, roomCode ?? string.Empty);
                throw new HubException(
                    "That room code is not valid. Codes are 4 characters, letters and digits.");

            case JoinStatus.RoomFull:
                throw new HubException(
                    $"Room {MatchManager.NormalizeCode(roomCode!)} already has two players.");
        }

        var handle = outcome.Handle!;
        await Groups.AddToGroupAsync(Context.ConnectionId, handle.MatchId);

        var (status, started) = handle.Locked(match =>
            (match.Status.ToString(), match.Status == MatchStatus.Active));

        return new JoinResultDto(handle.MatchId, outcome.Role.ToString(), status, started);
    }

    /// <summary>
    /// Records the player's held-direction intent.
    /// </summary>
    /// <remarks>
    /// Invalid values are REJECTED and logged, never clamped into gameplay - the constitution's
    /// Fair-Play requirements are explicit that out-of-range input must not be silently coerced.
    /// For the Hunter, the FR-007 frightened inversion is applied server-side when this intent is
    /// consumed by movement, so the client always sends the player's true intended direction.
    /// </remarks>
    public void SendInput(string direction)
    {
        var handle = matches.FindByConnection(Context.ConnectionId);
        if (handle is null)
        {
            return;
        }

        // Enum.TryParse also accepts NUMERIC strings, so "0" would quietly become Direction.None.
        // The contract defines exactly five literals; anything else - including a numeric alias
        // for one of them - is refused rather than interpreted.
        if (!IsAllowedDirection(direction) ||
            !Enum.TryParse<Direction>(direction, ignoreCase: false, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            log.InputRejected(handle.MatchId, Context.ConnectionId, direction);
            throw new HubException($"invalid direction '{direction}'");
        }

        handle.Locked(match =>
        {
            var player = PlayerFor(match, Context.ConnectionId);
            if (player is null || match.Status != MatchStatus.Active)
            {
                return;
            }

            player.DesiredDirection = parsed;
        });
    }

    /// <summary>
    /// FR-020: a disconnect ends the match immediately with a forfeit win for whoever is left.
    /// There is no reconnect grace period (clarified 2026-08-14).
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var handle = matches.FindByConnection(Context.ConnectionId);
        if (handle is not null)
        {
            var outcome = handle.Locked(match =>
            {
                var leaver = PlayerFor(match, Context.ConnectionId);
                if (leaver is null)
                {
                    return null;
                }

                leaver.Connected = false;
                log.PlayerDisconnected(match.MatchId, leaver.Role, Context.ConnectionId);

                // Only forfeit a match that was actually under way; abandoning a half-formed
                // lobby just frees the slot rather than awarding anyone a win.
                if (match.Status != MatchStatus.Active)
                {
                    match.Status = MatchStatus.Ended;
                    return null;
                }

                var winner = leaver.Role == Role.Runner ? Role.Hunter : Role.Runner;
                match.End(winner, MatchEndReason.Forfeit);

                if (match.Outcome is not null)
                {
                    log.MatchEnded(
                        match.MatchId, match.Outcome, match.ElapsedMs, match.Map.ClearedFraction);
                }

                return match.Outcome;
            });

            if (outcome is not null)
            {
                await Clients.Group(handle.MatchId).SendAsync(
                    "MatchEnded",
                    new OutcomeDto(
                        outcome.Winner.ToString(),
                        outcome.Reason.ToString(),
                        outcome.FinalPacmanScore,
                        outcome.FinalGhostScore));
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, handle.MatchId);
            matches.Remove(handle.MatchId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>The exact set of direction literals the contract permits.</summary>
    private static bool IsAllowedDirection(string value) => value is
        nameof(Direction.None) or
        nameof(Direction.Up) or
        nameof(Direction.Down) or
        nameof(Direction.Left) or
        nameof(Direction.Right);

    private static PlayerState? PlayerFor(MatchState match, string connectionId)
    {
        if (match.Pacman?.ConnectionId == connectionId)
        {
            return match.Pacman;
        }

        return match.Ghost?.ConnectionId == connectionId ? match.Ghost : null;
    }
}

/// <summary>Returned from <see cref="MatchHub.JoinMatch"/> so the client knows which role it plays.</summary>
public sealed record JoinResultDto(string MatchId, string Role, string Status, bool Started);
