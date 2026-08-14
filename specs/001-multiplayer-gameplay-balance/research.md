# Phase 0 Research: 1v1 Asymmetric Multiplayer Gameplay & Balance Rules

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

This project had no pre-existing codebase, tech stack, or prior architectural decisions to build
on, so every item below was an open unknown in Technical Context. Each is resolved here against
the constraints that actually matter for this feature: Constitution Principle III
(server-authoritative state), spec SC-006 (≤100ms input-to-effect latency), and Principle V
(scope discipline — no unneeded infrastructure).

## 1. Real-time networking model

**Decision**: ASP.NET Core with SignalR Hubs on the backend, communicating with clients over
WebSocket, with the backend as sole owner of gameplay state.

**Rationale**: Constitution Principle III makes server authority non-negotiable — the entire
balance model (a 5% base speed differential, a 3-tile anti-camping radius) is too fine-grained to
survive a client that can misreport its own position. SignalR is the standard real-time transport
for ASP.NET Core (the backend's mandated platform per user direction): it provides Hub Groups
(one group per match), WebSocket transport with automatic fallback, and connection lifecycle
events (`OnDisconnectedAsync`) that map directly onto this feature's match structure (FR-014
timer, FR-020 disconnect handling). Unlike Colyseus (Node-only), SignalR has no built-in
per-client state-diffing/schema-filtering, so that responsibility is taken on explicitly in the
Hub layer (see `contracts/match-room-protocol.md`) rather than assumed from the framework.

**Alternatives considered**:
- *Raw ASP.NET Core WebSockets (`System.Net.WebSockets`) with a hand-rolled protocol*: full
  control, but SignalR already provides Hub method dispatch, group management, and reconnection
  primitives on top of the same WebSocket transport — reimplementing that is unjustified
  complexity against Principle V (YAGNI).
- *Peer-to-peer (WebRTC data channels, no server authority)*: rejected outright — directly violates
  Principle III, since either peer could misreport position/speed with no independent check.
- *HTTP polling*: rejected — cannot meet the ≤100ms input-to-effect budget (SC-006) at any
  reasonable polling interval without excessive request volume.

## 2. Language & runtime

**Decision**: C# / .NET 10 LTS for the backend (per explicit user direction); TypeScript 5.x for the
browser frontend, since gameplay logic and rendering are cleanly separated by Principle III (the
frontend never makes gameplay decisions, so it does not need to share a runtime with the backend).

**Rationale**: The backend owns 100% of gameplay-affecting logic (Principle III), so its language
choice is independent of the frontend's — the two only need to agree on a wire contract (see
`contracts/match-room-protocol.md`) and on numeric balance constants (Section 5 below), not on a
shared language or shared code. .NET 10 is the current LTS release with mature WebSocket/SignalR
support and strong static typing, which — like the previous TypeScript-only design — gives an
early, cheap check on Principle II (deterministic rule resolution): a state-machine transition
(e.g., Frightened → Eyes-Only) that's missing a case is a compile error, not a runtime surprise
discovered in a match.

**Alternatives considered**:
- *Node.js/TypeScript backend (original Phase 0 decision)*: would have allowed one language across
  the whole stack and direct code sharing via `shared/`; superseded by explicit user direction to
  use .NET for the backend. The design compensates for the lost same-language sharing via the
  language-neutral JSON constants source in Section 5.
- *ASP.NET Core Minimal API + raw WebSockets, no SignalR*: rejected per Section 1 above — SignalR's
  Hub/Group primitives are a direct fit and not extra complexity for this use case.

## 3. Frontend application framework & rendering approach

**Decision**: React (per explicit user direction) for the application shell, screens (join/lobby,
match, results), and HUD — score, timer, lives, sonar pulse indicator, anti-camping speed-boost
indicator — as ordinary React components driven by state from the SignalR connection. The game
*board* itself (tile grid, Pac-Man/Ghost sprites, Frightened visual state) is drawn with the native
HTML5 Canvas 2D API inside a single `<canvas>` React mounts once; an imperative draw loop (started
in a `useEffect`, reading from a ref-held snapshot of the latest `StateUpdate`) paints each frame,
rather than each tile/sprite being a re-rendered React element.

**Rationale**: React is a natural fit for the HUD and screen-level UI — discrete, event-driven,
state-transition-shaped, exactly what React's component model is for — and the user has directed
its use. It is a poor fit for the board itself: re-rendering a React tree for two independently
moving actors at 60fps, on every authoritative tick, adds virtual-DOM diff overhead the SC-006
100ms latency budget doesn't have room for, and buys nothing a raw pixel-blit doesn't already do
better. Splitting along this line — React owns everything except the live board, Canvas owns only
the board — keeps both tools doing what they're good at without forcing the whole frontend through
one paradigm for the sake of consistency alone. This is a common, well-established pattern for
React-hosted real-time canvas games (React mounts and sizes the canvas; a plain imperative loop
owns pixels inside it).

**Alternatives considered**:
- *React re-rendering the board as SVG/DOM elements per tile/sprite*: rejected — measurably worse
  performance headroom at 60fps with two independently-moving actors than a single canvas draw
  call per frame; DOM-based rendering was already rejected in the original (pre-React) version of
  this decision for the same reason.
- *A React-canvas binding library (e.g., react-konva)*: adds a dependency and an abstraction layer
  over Canvas that this feature's simple tile/sprite/HUD-overlay needs don't require — against
  Principle V (YAGNI); a plain `useRef` + imperative draw loop is fewer moving parts for the same
  result.
- *Phaser or similar 2D engine, wrapped inside a React shell*: reasonable if the game were expected
  to grow many more visual systems soon, but nothing in the current spec calls for that, and it can
  be adopted later without touching the authoritative server code or the React shell if it ever
  becomes justified.

## 4. Testing strategy

**Decision**: A three-layer test strategy. (1) xUnit for pure-class/method unit tests of every
rule in `backend/src/MatchServer/Engine/` (movement/speed, collision + simultaneity resolution,
vision/sonar, anti-camping, scoring, win evaluation). (2) `Microsoft.AspNetCore.SignalR.Client`
driven against an in-memory ASP.NET Core `TestServer` for integration tests that connect two
simulated Hub clients and drive a match through full acceptance scenarios from `spec.md`, plus
Vitest + React Testing Library on the frontend for HUD/screen component tests and
input-mapping/canvas-draw-loop unit tests. (3) **Playwright** (per explicit user direction) for
full-stack end-to-end tests that launch two real browser contexts against the real running
backend + frontend, one per role, and drive each `quickstart.md` validation scenario exactly as a
human tester would — keyboard input, on-screen HUD assertions, and match-outcome assertions —
closing the gap between "the rule is correct in isolation" and "two actual browsers playing an
actual match observe the correct outcome."

**Rationale**: Constitution Principle II requires every rule — especially simultaneity/edge cases
like FR-021 — to have one documented, testable outcome. Isolating rule logic as plain C# classes in
`Engine/` (no direct dependency on `MatchHub` or SignalR) makes each spec acceptance scenario
individually unit-testable without spinning up networking; the Hub-integration layer then confirms
the Hub wiring actually calls those classes correctly end-to-end (e.g., that a disconnect really
triggers FR-020's forfeit path through the real Hub lifecycle, not just the isolated rule class).
Playwright adds the layer neither of those two can cover on its own: real WebSocket transport
timing, real two-client fog-of-war filtering (does the Hunter's actual browser ever receive the
Runner's true position over the wire?), and real rendering — i.e., verification against SC-006's
latency budget and the fairness guarantees in Constitution Principle III as actually experienced by
two browsers, not simulated ones.

**Alternatives considered**:
- *NUnit or MSTest instead of xUnit*: comparable capability; xUnit chosen as the current default
  for new .NET projects and first-class `Microsoft.AspNetCore.Mvc.Testing`/`TestServer` support —
  not a load-bearing decision, swappable later without affecting this plan's other conclusions.
- *Cypress instead of Playwright*: comparable capability for single-browser-context testing, but
  Playwright's native multi-context API (one `BrowserContext` per simulated player, in a single
  test process) maps directly onto this feature's inherent two-client structure, which Cypress
  handles less directly.
- *No end-to-end layer, rely on unit + Hub-integration tests only*: was the original Phase 0
  position (deemed unnecessary for rule-correctness verification); superseded by explicit user
  direction, and on reflection genuinely closes a real gap — the fog-of-war filtering and latency
  requirements are properties of the full wire path, not of any single layer's isolated logic.

## 5. Shared constants & types

**Decision**: A single, language-neutral `shared/balance-constants.json` holding every spec-derived
numeric balance constant (`pacmanBaseSpeed: 1.00`, `ghostBaseSpeed: 0.95`,
`frightenedDurationMs: 8000`, `frightenedInversionMs: 3000`, `ghostSpeedFrightened: 0.70`,
`eyesSpeed: 1.50`, `ghostHouseLockoutMs: 5000`, `visionRadiusTiles: 6`, `sonarIntervalMs: 4000`,
`campRadiusTiles: 3`, `campTriggerMs: 5000`, `campSpeedPenalty: 0.15`, `matchDurationMs: 180000`,
`clearThresholdPct: 0.70`, point values from FR-018, and `maxInputLatencyMs: 100` from SC-006),
with a small `shared/codegen` script that generates a read-only `BalanceConstants.cs` static class
for the backend and a `balanceConstants.ts` module for the frontend from that one JSON file as a
build step.

**Rationale**: With a C# backend and a TypeScript frontend, the two can no longer share a literal
source file the way an all-TypeScript stack could — this JSON-plus-codegen approach is the
cross-language equivalent of Constitution Principle I's "one edit site" requirement: a future
balance PR edits `balance-constants.json` once, regenerates both language bindings as part of the
build, and it is structurally impossible for the backend's number and the frontend's number to
diverge (the generated files are never hand-edited, so there's no second place to forget).

**Alternatives considered**:
- *Hand-maintain equivalent constants separately in C# and TypeScript*: rejected — this is exactly
  the drift risk Principle I exists to prevent, now worse because a language mismatch makes the
  duplication less visually obvious in review (e.g. `0.95` vs. `0.95f` sitting in unrelated files).
- *Server-only constants, client reads values from initial state sync*: viable for pure gameplay
  values, but HUD-only derived thresholds (e.g., when to show the sonar pulse UI) still benefit
  from a shared, named constant instead of a magic number in render code; the JSON source covers
  both cases uniformly.
- *A tiny internal NuGet + npm package pair published from one source*: heavier tooling (private
  registry or workspace publishing) than a single JSON file plus a build-time codegen script
  warrants at this project's size — against Principle V (YAGNI); revisit if more languages/packages
  ever need the same constants.

## Outcome

All Technical Context items are resolved; no `NEEDS CLARIFICATION` markers remain. Proceeding to
Phase 1 design.
