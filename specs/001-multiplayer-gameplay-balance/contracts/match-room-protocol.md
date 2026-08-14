# Contract: MatchHub Real-Time Protocol

**Feature**: [../spec.md](../spec.md) | **Data Model**: [../data-model.md](../data-model.md)

This feature exposes one external interface: the ASP.NET Core SignalR `MatchHub` that two browser
clients connect to for a single match. This document is the contract between
`backend/src/MatchServer/Hubs/MatchHub.cs` and `frontend/src/net` — the frontend MUST NOT assume
any gameplay behavior beyond what is described here, and the backend MUST NOT change these shapes
without updating this contract and the corresponding spec requirement.

Unlike a state-diffing framework, SignalR has no built-in per-client filtering or automatic schema
sync — every message below is an explicit Hub method (client → server) or client-side handler
(server → client) that the backend must call out deliberately. This is treated as a feature, not a
gap: it forces the fog-of-war filtering (FR-011) to be an explicit, reviewable step in the Hub
code rather than implicit framework behavior.

## Connection lifecycle

1. Client connects to the `/hubs/match` endpoint and invokes `JoinMatch(roomCode)`.
   - `roomCode` is **nullable but not omittable** — SignalR does not fill C# optional parameters,
     so the client must send `null` explicitly to auto-match.
   - **`null`** → paired with whoever is waiting in a public room.
   - **A 4-character code** → joins that specific room, creating it if nobody has yet. This is how
     two people play *each other* rather than whoever happens to click first.
2. The backend assigns `role` (`Runner` first, `Hunter` second) and adds the connection to a
   SignalR Group named for the match. The group name **is** the room code.

### Room codes

- Four characters from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` — uppercase and digits, minus `I`, `O`,
  `0`, and `1`, which are the pairs people misread when reading a code off a screen or aloud.
- Case-insensitive, and surrounding whitespace is trimmed, so a pasted code works.
- Every room has one, including auto-matched rooms: it is returned in `JoinResult.matchId` so a
  waiting player can share it and pull in a specific friend instead of waiting on a stranger.
- **Rooms opened with a code are private**: auto-matching skips them. Otherwise a stranger would
  take the slot being held for the friend, who would then find their own room "full".

`JoinMatch` throws a `HubException` when:

| Condition | Message |
|---|---|
| Code is not 4 valid characters | "That room code is not valid. Codes are 4 characters, letters and digits." |
| Room already has two players | "Room `XXXX` already has two players." |

A rejection is per-message: the connection stays usable and the client can retry with a different
code. Joining a full room is **refused rather than rerouted** — silently matching the player
against a stranger would be worse than an error, since they asked for a specific opponent.
3. `Match.status` becomes `Active` once both roles are filled; the 180,000ms timer (FR-014) starts
   at that moment, and the backend begins its ~30Hz tick loop (per `plan.md` Performance Goals)
   for that match.
4. On `MatchHub.OnDisconnectedAsync`, the backend MUST resolve this as an immediate forfeit per
   FR-020 (see "Server → Client: `MatchEnded`" below) — no reconnect grace period (per the
   2026-08-14 clarification session recorded in `spec.md`).

## Client → Server: Hub methods

### `SendInput(direction: string)`

Invoked whenever the local player's held-direction changes (not once per render frame).

- `direction` MUST be one of `"Up"`, `"Down"`, `"Left"`, `"Right"`, `"None"`.
- The backend validates this server-side; any other value is rejected and logged, not clamped
  (constitution Fair-Play requirement — reject, don't silently coerce into gameplay).
- For the Hunter role, the backend — not the client — applies the FR-007 input inversion during
  the first 3.0s of a Frightened window. The client always sends the player's true intended
  direction; inversion is a server-side effect on how that intent is applied to movement, never a
  client-side responsibility (Constitution Principle III).

## Server → Client: state sync

### `StateUpdate(state: MatchStateDto)`

Broadcast once per authoritative tick (~30Hz) to each connected client, carrying the fields defined
in `data-model.md` for `Match`, `Player` (both roles), `Map`/`Pellet`/`PowerPellet`, and
`FrightenedState`.

**Fog-of-war filtering (FR-011) is mandatory and per-recipient**: the backend MUST compute two
distinct `MatchStateDto` payloads per tick — one sent to the Runner's connection with full map/
opponent visibility, and one sent to the Hunter's connection with the Runner's position/facing
omitted (or coarsened, per FR-011's radius/line-of-sight rule) whenever the Runner is not
currently visible to the Hunter. The Hunter's client MUST NOT receive the Runner's true position
in any payload during those ticks — this is enforced by omission at the source, not by asking the
client to hide data it already has (a direct consequence of Constitution Principle III: data a
client shouldn't use but received anyway is an extractable fairness bug, not a UI concern).

## Server → Client: discrete events

Sent via `IHubContext`/client proxy method invocation alongside the per-tick `StateUpdate`, for
events a client should react to once rather than infer from a state diff:

### `SonarPulse(quadrant: string)` — Hunter connection only

`quadrant` is one of `"NE"`, `"NW"`, `"SE"`, `"SW"`. Sent every `sonarIntervalMs` (4000ms, from
`shared/balance-constants.json`) while the Runner is outside the Hunter's direct visibility
(FR-011). Never includes exact coordinates — only the approximate quadrant.

**Quadrant origin is map-relative, not Hunter-relative**: the four quadrants are fixed regions of
the map determined by its horizontal and vertical midlines, so the value conveys no bearing or
distance from the Hunter's own position (FR-011). A Hunter-relative quadrant would function as a
direction-to-target indicator and would erode the vision disadvantage this rule exists to create.

### `ScoreEvent(eventType: string, points: int, recipient: string)`

- `eventType` is one of `"PelletCollected"`, `"PowerPelletCollected"`, `"GhostCaught"`,
  `"PacmanEliminated"`, `"TimeBonus"` (matches the FR-018 scoring matrix rows).
- `points` is one of `10, 50, 200, 400, 800, 1600, 500` depending on `eventType`.
- `recipient` is `"Pacman"` or `"Ghost"`.

Sent to both clients within the SC-005 1-second live-update budget, so HUDs can show a scoring
flourish, not just a number that changed.

### `MatchEnded(winner: string, reason: string, finalPacmanScore: int, finalGhostScore: int)`

- `winner` is `"Pacman"` or `"Ghost"` — never omitted (SC-001: every match reaches a definitive
  outcome).
- `reason` is one of `"PelletsCleared"`, `"LivesDepleted"`, `"TimeoutClearThresholdMet"`,
  `"TimeoutClearThresholdMissed"`, `"Forfeit"` — matches the `Outcome` entity in `data-model.md`
  exactly, and maps 1:1 to FR-015/FR-016/FR-017(+FR-023)/FR-020.

Terminal event for the match; sent once, immediately followed by the backend removing both
connections from the match's SignalR Group and disposing the match's in-memory state.

## Non-goals of this contract

Matchmaking/lobby protocol, spectator connections, and reconnection-after-disconnect are explicitly
out of scope (spec Assumptions; FR-020 defines disconnect as immediate forfeit, not a
reconnect-eligible state) and have no Hub methods or events defined here.
