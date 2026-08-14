using System.Collections.Concurrent;
using MatchServer.State;

namespace MatchServer.Engine;

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
    private readonly ConcurrentDictionary<string, MatchHandle> _matches = new();

    public IReadOnlyCollection<MatchHandle> ActiveMatches => _matches.Values.ToList();

    /// <summary>
    /// Finds a match still waiting for a second player, or creates one.
    /// First joiner becomes the Runner, second the Hunter (contract connection lifecycle).
    /// </summary>
    public MatchHandle JoinOrCreate(string connectionId, out Role assignedRole)
    {
        foreach (var candidate in _matches.Values)
        {
            var joined = candidate.Locked(match =>
            {
                if (match.Status != MatchStatus.WaitingForPlayers || match.Ghost is not null)
                {
                    return (Role?)null;
                }

                match.Ghost = PlayerState.CreateHunter(
                    connectionId, match.Map.GhostHouse.X, match.Map.GhostHouse.Y);
                log.PlayerJoined(match.MatchId, Role.Hunter, connectionId);

                match.Status = MatchStatus.Active;
                log.MatchStarted(match.MatchId);
                return Role.Hunter;
            });

            if (joined is not null)
            {
                assignedRole = joined.Value;
                return candidate;
            }
        }

        var matchId = Guid.NewGuid().ToString("n")[..8];
        var map = FixedMap.Create();
        var state = new MatchState { MatchId = matchId, Map = map };
        state.Pacman = PlayerState.CreateRunner(connectionId, map.RunnerSpawn.X, map.RunnerSpawn.Y);

        var handle = new MatchHandle(state);
        _matches[matchId] = handle;

        log.MatchCreated(matchId, map.MapId);
        log.PlayerJoined(matchId, Role.Runner, connectionId);

        assignedRole = Role.Runner;
        return handle;
    }

    public MatchHandle? Find(string matchId) =>
        _matches.TryGetValue(matchId, out var handle) ? handle : null;

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
