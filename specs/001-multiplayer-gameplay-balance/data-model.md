# Phase 1 Data Model: 1v1 Asymmetric Multiplayer Gameplay & Balance Rules

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Entities below extract and elaborate the spec's Key Entities section into fields, relationships,
and state transitions, using the shared constants named in `research.md` §5. This is a logical
model — realized as C# POCOs under `backend/src/MatchServer/State/` and mirrored as TypeScript
DTOs on the frontend per the wire contract in `contracts/match-room-protocol.md` — not a
persistence/database schema; nothing here is persisted beyond a match's lifetime, per the spec's
Assumptions.

## Match

The root aggregate for one game session; one Match exists per SignalR match Group, holds exactly
two Players, and owns the timer, map/pellet state, and outcome.

| Field | Type | Notes |
|---|---|---|
| `matchId` | string | SignalR Group identifier for this match |
| `status` | enum: `Active`, `Ended` | Starts `Active` once both players have joined the match |
| `elapsedMs` | integer, 0–180000 | Server-ticked; match ends when it reaches `MATCH_DURATION_MS` (FR-014) |
| `pacman` | Player (role = Runner) | See Player below |
| `ghost` | Player (role = Hunter) | See Player below |
| `map` | Map | Fixed single map (FR-022); pellet/power-pellet layout and collision geometry |
| `frightened` | FrightenedState \| null | Present only while a Frightened window is active |
| `scoreChain` | integer, 0–4 | Consecutive-catch counter for the 200/400/800/1600 sequence (FR-009); resets to 0 when Frightened State ends without further catches or when a new Frightened window begins |
| `outcome` | Outcome \| null | Set once, when `status` becomes `Ended` |

**State transitions** (`status`):
`Active → Ended` triggered by exactly one of: Pac-Man 100%-clear (FR-015), Pac-Man lives reach 0
(FR-016), timer reaches 0 with evaluation per FR-017/FR-023, or a player disconnect (FR-020). No
other transition exists; `Ended` is terminal for the room.

## Player

Represents one connected human occupying one of the two fixed roles for the match's duration
(no mid-match role swap — spec Assumptions).

| Field | Type | Notes |
|---|---|---|
| `connectionId` | string | SignalR connection identifier for this client |
| `role` | enum: `Runner`, `Hunter` | Fixed for the match |
| `x`, `y` | float (tile coordinates) | Authoritative position, server-computed only (Constitution III) |
| `facing` | enum: `Up`, `Down`, `Left`, `Right` | Current movement heading |
| `speedMultiplier` | float | Derived, not client-set: `1.00` for Runner always (FR-001); for Hunter, one of `0.95` (normal), `0.80` (anti-camping, FR-012), `0.70` (frightened, FR-006), `1.50` (eyes-only, FR-009) |
| `livesRemaining` | integer, 0–3 | Runner only; starts at 3 (FR-002); Hunter has no lives field (unlimited respawns, FR-003) |
| `ghostSubState` | enum: `Normal`, `Frightened`, `EyesOnly`, `Respawning` | Hunter only |
| `respawnReadyAtMs` | integer \| null | Hunter only; set on elimination, gates return to `Normal` (5s normal-death delay FR-003, or 5s Ghost House lockout after EyesOnly per FR-009) |
| `connected` | boolean | Flips to `false` on socket drop; triggers FR-020 forfeit resolution for `Match.status` |
| `score` | integer, ≥0 | Real-time accumulated points per FR-018 |

**State transitions** (`ghostSubState`, Hunter only):
`Normal → Frightened` (Runner consumes a Power Pellet, FR-005) → `EyesOnly` (Runner catches
Hunter while `Frightened`, FR-009) → `Respawning` (reaches Ghost House, 5.0s lockout) → `Normal`.
`Frightened → Normal` directly if the 8.0s window elapses uncaught. A same-instant collision with
a `Normal`-state Runner-elimination event is resolved per FR-021 (elimination takes precedence;
see Edge Case entity note under FrightenedState).

## Map

| Field | Type | Notes |
|---|---|---|
| `mapId` | string | Fixed single value for this feature (FR-022) |
| `tiles` | 2D grid | Walkable/wall geometry; grid-based movement (spec Assumptions) |
| `pellets` | Pellet[] | Regular pellets, +10 each (FR-018) |
| `powerPellets` | PowerPellet[] | Power pellets, +50 each (FR-018) |
| `totalPelletCount` | integer, derived | Denominator for the 70%/100% clear thresholds (FR-015, FR-017) |

## Pellet

| Field | Type | Notes |
|---|---|---|
| `x`, `y` | integer (tile coordinates) | Fixed position on `map.tiles` |
| `collected` | boolean | Set true when Runner occupies the tile; irreversible for the match |

## PowerPellet

Extends Pellet with:

| Field | Type | Notes |
|---|---|---|
| `campTimerMs` | integer, resets on Hunter exit or on collection | Drives the FR-012 anti-camping trigger; only meaningful while `collected = false` |
| `campDebuffActive` | boolean | Mirrors whether this specific pellet's zone currently has the Hunter's anti-camping debuff applied (only one can be true at a time, since the Hunter occupies one position) |

## FrightenedState

| Field | Type | Notes |
|---|---|---|
| `startedAtMs` | integer | Match-relative timestamp; resets (not stacks) on a second Power Pellet pickup (FR-005) |
| `expiresAtMs` | integer | `startedAtMs + FRIGHTENED_DURATION_MS` |
| `inversionExpiresAtMs` | integer | `startedAtMs + FRIGHTENED_INVERSION_MS`; while `elapsedMs < inversionExpiresAtMs`, Hunter directional input is inverted (FR-007) |

**Edge-case note (FR-021)**: if a Runner-Hunter contact in `Normal` sub-state and a Power-Pellet
pickup are both attributable to the same simulation tick, the tick processes the elimination
(Player.livesRemaining decrement) before evaluating the pickup; `FrightenedState` is still created
from the pickup, but it starts from the Hunter's next `Normal`-substate tick after
`respawnReadyAtMs`, not retroactively during the tick where the life was lost.

## Outcome

| Field | Type | Notes |
|---|---|---|
| `winner` | enum: `Pacman`, `Ghost` | Never null once `Match.status = Ended` (SC-001: always definitive) |
| `reason` | enum: `PelletsCleared`, `LivesDepleted`, `TimeoutClearThresholdMet`, `TimeoutClearThresholdMissed`, `Forfeit` | Maps 1:1 to FR-015/FR-016/FR-017(+FR-023)/FR-020 |
| `finalPacmanScore`, `finalGhostScore` | integer | Snapshot at match end, per FR-018 |

## ScoreEvent (transient, not persisted on Match)

Represents one point-earning action as it's broadcast to clients for HUD/log purposes (FR-019);
not stored as a list on `Match` — score events are folded into `Player.score` immediately and
only need to exist as an outbound message (see `contracts/`).

| Field | Type | Notes |
|---|---|---|
| `type` | enum: `PelletCollected`, `PowerPelletCollected`, `GhostCaught`, `PacmanEliminated`, `TimeBonus` | Matches the FR-018 scoring matrix rows |
| `points` | integer | For `GhostCaught`, one of 200/400/800/1600 depending on `Match.scoreChain` at catch time |
| `recipient` | enum: `Pacman`, `Ghost` | Per FR-018 |
