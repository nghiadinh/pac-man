using System.Collections.Concurrent;
using MatchServer.State;

namespace MatchServer.Engine;

/// <summary>Why a join attempt failed, or that it succeeded.</summary>
public enum JoinStatus
{
    Joined,
    RoomFull,
    InvalidRoomCode,
}

/// <summary>Result of a join attempt. <see cref="Handle"/> is set only when <see cref="Status"/> is Joined.</summary>
public sealed record JoinOutcome(JoinStatus Status, MatchHandle? Handle = null, Role Role = Role.Runner)
{
    public static JoinOutcome Success(MatchHandle handle, Role role) =>
        new(JoinStatus.Joined, handle, role);

    public static readonly JoinOutcome Full = new(JoinStatus.RoomFull);
    public static readonly JoinOutcome BadCode = new(JoinStatus.InvalidRoomCode);
}

/// <summary>
/// Owns every in-memory match. There is no persistence: match state lives for the duration of the
/// match and is disposed when it ends (spec Assumptions place match history out of scope).
/// </summary>
/// <remarks>
/// Each match is mutated only from the tick loop or from a hub callback, and every such mutation
/// takes the match's own lock via <see cref="MatchHandle.Locked"/>. That keeps each match's rule
/// evaluation single-threaded and ordered, which is what Constitution Principle II
/// (deterministic rule resolution) requires - two ticks must never interleave mid-pipeline.
/// </remarks>
public sealed class MatchManager(MatchLogger log)
{
    /// <summary>
    /// Characters used in room codes: uppercase and digits, minus the pairs people misread when
    /// reading a code aloud or off a screen (I/1, O/0).
    /// </summary>
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const int CodeLength = 4;

    private readonly ConcurrentDictionary<string, MatchHandle> _matches = new();

    /// <summary>
    /// Serialises the whole join operation.
    /// </summary>
    /// <remarks>
    /// A ConcurrentDictionary makes each individual access safe, but joining is a COMPOUND
    /// operation - look for an open match, and create one only if there isn't one. Without this
    /// gate two players clicking Join at the same instant can both observe no open match, both
    /// create their own, and both sit waiting forever while each is the other's opponent.
    /// The per-match locks cannot prevent that: the race happens between matches, not inside one.
    /// </remarks>
    private readonly Lock _joinGate = new();

    public IReadOnlyCollection<MatchHandle> ActiveMatches => _matches.Values.ToList();

    /// <summary>
    /// Joins a match, or creates one. Pass a <paramref name="roomCode"/> to play with a specific
    /// person; omit it to be paired with whoever is waiting.
    /// </summary>
    public JoinOutcome JoinOrCreate(string connectionId, string? roomCode = null)
    {
        // One gate for the entire scan-or-create sequence. Taking a match's own lock inside this
        // is safe: nothing that holds a match lock ever waits on this gate, so there is no cycle.
        lock (_joinGate)
        {
            return string.IsNullOrWhiteSpace(roomCode)
                ? JoinAnyOpenRoom(connectionId)
                : JoinNamedRoom(connectionId, roomCode);
        }
    }

    /// <summary>Auto-match: take the first room still waiting for a second player.</summary>
    private JoinOutcome JoinAnyOpenRoom(string connectionId)
    {
        foreach (var candidate in _matches.Values)
        {
            // Skip private rooms: their code was shared with a specific person, and filling the
            // slot with a stranger would leave that person locked out of their own room.
            if (candidate.Locked(m => m.IsPrivate))
            {
                continue;
            }

            if (TryFillHunterSlot(candidate, connectionId))
            {
                return JoinOutcome.Success(candidate, Role.Hunter);
            }
        }

        return JoinOutcome.Success(
            CreateRoom(connectionId, GenerateCode(), isPrivate: false), Role.Runner);
    }

    /// <summary>
    /// Join by code: takes the named room if it is waiting, or opens it if nobody has yet. That
    /// second case is what lets two people agree on a code beforehand and both "join" it -
    /// whoever arrives first creates it.
    /// </summary>
    private JoinOutcome JoinNamedRoom(string connectionId, string roomCode)
    {
        var code = NormalizeCode(roomCode);

        if (!IsValidCode(code))
        {
            return JoinOutcome.BadCode;
        }

        if (_matches.TryGetValue(code, out var existing))
        {
            return TryFillHunterSlot(existing, connectionId)
                ? JoinOutcome.Success(existing, Role.Hunter)
                : JoinOutcome.Full; // already has two players, or is under way
        }

        return JoinOutcome.Success(CreateRoom(connectionId, code, isPrivate: true), Role.Runner);
    }

    /// <summary>Fills the Hunter slot if it is open, starting the match. Returns false otherwise.</summary>
    private bool TryFillHunterSlot(MatchHandle handle, string connectionId) =>
        handle.Locked(match =>
        {
            if (match.Status != MatchStatus.WaitingForPlayers || match.Ghost is not null)
            {
                return false;
            }

            match.Ghost = PlayerState.CreateHunter(
                connectionId, match.Map.GhostHouse.X, match.Map.GhostHouse.Y);
            log.PlayerJoined(match.MatchId, Role.Hunter, connectionId);

            match.Status = MatchStatus.Active;
            log.MatchStarted(match.MatchId);
            return true;
        });

    private MatchHandle CreateRoom(string connectionId, string code, bool isPrivate)
    {
        var map = FixedMap.Create();
        var state = new MatchState { MatchId = code, Map = map, IsPrivate = isPrivate };
        state.Pacman = PlayerState.CreateRunner(connectionId, map.RunnerSpawn.X, map.RunnerSpawn.Y);

        var handle = new MatchHandle(state);
        _matches[code] = handle;

        log.MatchCreated(code, map.MapId);
        log.PlayerJoined(code, Role.Runner, connectionId);

        return handle;
    }

    /// <summary>Generates a code that no live room is using.</summary>
    private string GenerateCode()
    {
        // The room count is tiny next to the 32^4 space, so a free code is found immediately;
        // the bound only stops a pathological loop if that ever stops being true.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var code = string.Create(CodeLength, 0, (span, _) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    span[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];
                }
            });

            if (!_matches.ContainsKey(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException(
            "could not allocate a free room code after 100 attempts");
    }

    /// <summary>Codes are case-insensitive and tolerate surrounding whitespace when pasted.</summary>
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static bool IsValidCode(string code) =>
        code.Length == CodeLength && code.All(CodeAlphabet.Contains);

    public MatchHandle? Find(string matchId) =>
        _matches.TryGetValue(NormalizeCode(matchId), out var handle) ? handle : null;

    /// <summary>Finds the match a connection belongs to, if any.</summary>
    public MatchHandle? FindByConnection(string connectionId) =>
        _matches.Values.FirstOrDefault(h => h.Locked(m =>
            m.Pacman?.ConnectionId == connectionId || m.Ghost?.ConnectionId == connectionId));

    public void Remove(string matchId) => _matches.TryRemove(matchId, out _);
}

/// <summary>
/// A match plus the lock guarding it. All access goes through <see cref="Locked{T}"/> so no two
/// threads can observe or mutate a match mid-pipeline.
/// </summary>
public sealed class MatchHandle(MatchState state)
{
    private readonly Lock _gate = new();

    public string MatchId => state.MatchId;

    /// <summary>Runs <paramref name="action"/> with exclusive access to the match state.</summary>
    public T Locked<T>(Func<MatchState, T> action)
    {
        lock (_gate)
        {
            return action(state);
        }
    }

    public void Locked(Action<MatchState> action)
    {
        lock (_gate)
        {
            action(state);
        }
    }
}
