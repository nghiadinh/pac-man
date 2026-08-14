# Implementation Plan: 1v1 Asymmetric Multiplayer Gameplay & Balance Rules

**Branch**: `001-multiplayer-gameplay-balance` | **Date**: 2026-08-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-multiplayer-gameplay-balance/spec.md`

## Summary

Build the server-authoritative match engine and browser client for a 1v1 web-based Pac-Man game
where one player controls Pac-Man (Runner) and the other controls a human Ghost (Hunter), governed
by the asymmetric speed, vision, power-pellet, anti-camping, and scoring rules defined in
`spec.md` (FR-001–FR-023). Technical approach: an **ASP.NET Core (.NET) backend** using **SignalR**
as the real-time WebSocket transport, running an authoritative per-match game loop that owns all
gameplay-affecting state per Constitution Principle III, paired with a **React** TypeScript client
— React for the app shell/screens/HUD, a plain Canvas draw loop for the live board — that renders
server state and forwards player input — no game logic is trusted client-side.

## Technical Context

**Language/Version**: C# / .NET 10 LTS (backend); TypeScript 5.x via Vite build (frontend, browsers)

**Primary Dependencies**: ASP.NET Core + SignalR (WebSocket real-time transport and Hub programming
model) on the backend; React (application shell, screens, HUD) + `@microsoft/signalr` client SDK
+ native HTML5 Canvas 2D API (live match board only) on the frontend, built with Vite (no heavy
game engine — the tile-based 2D board doesn't need one)

**Storage**: N/A — match state is held in memory (in-process) for the lifetime of a match; no
persistent database, since the spec's Assumptions place matchmaking, accounts, and match history
out of scope

**Testing**: xUnit for pure game-rule unit tests (speed/collision/scoring/win-condition logic in
isolation) on the backend; `Microsoft.AspNetCore.SignalR.Client` against an in-memory `TestServer`
for Hub integration tests that drive a full match through two simulated connections; Vitest +
React Testing Library for frontend HUD/screen component tests and input-mapping/canvas-draw-loop
unit tests; Playwright for full-stack end-to-end tests driving two real browser contexts (one per
role) against the real backend + frontend, automating the `quickstart.md` validation scenarios

**Target Platform**: Backend: ASP.NET Core on .NET 10, deployable to any .NET-10-compatible host
(Linux or Windows). Client: latest two versions of Chrome/Firefox/Edge/Safari on desktop

**Project Type**: Web application (frontend + backend + a small language-neutral shared constants
source so balance numbers can't drift between a C# backend and a TypeScript frontend)

**Performance Goals**: Server authoritative simulation tick at ~30Hz (≈33ms/tick); client renders
at 60fps using interpolation between authoritative ticks; combined with network transit this must
fit inside the SC-006 100ms input-to-effect budget

**Constraints**: Round-trip input-to-effect latency ≤100ms (spec SC-006 / constitution Fair-Play
requirement); all movement, collision, timer, and scoring decisions computed server-side only
(constitution Principle III); single fixed map, single match per game session (spec FR-022,
Assumptions)

**Scale/Scope**: One active match = one SignalR Hub group with exactly 2 connected clients; the
backend process hosts many such groups concurrently (standard ASP.NET Core connection handling),
but the spec defines no specific concurrent-match volume target — capacity planning is deferred to
a future spec if needed

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Competitive Fairness by Design | PASS | Every balance constant used in this plan (speeds, durations, radii, point values) is taken verbatim from `spec.md` FR-001–FR-023; the plan introduces no new or altered balance constant. Because the backend (C#) and frontend (TypeScript) no longer share a language, the constants are defined once in a language-neutral `shared/balance-constants.json` and code-generated into both, preserving "one edit site" (see `research.md` §5). |
| II. Deterministic Rule Resolution | PASS | Each match's game loop runs single-threaded within its own scope on the server; FR-021 (simultaneous elimination vs. pellet pickup) and FR-023 (score tie-break) already specify the one deterministic outcome the loop applies in a fixed evaluation order. Verified at three layers per `research.md` §4: xUnit (rule logic in isolation), Hub-integration (`TestServer`), and Playwright end-to-end (two real browsers observe the same documented outcome). |
| III. Server-Authoritative State (NON-NEGOTIABLE) | PASS | Chosen architecture is authoritative-server-by-construction: clients invoke a single `SendInput` Hub method with their intended direction only; the backend computes and broadcasts position, collisions, timers, scores, and win/loss. |
| IV. Spec-First Development | PASS | Every entity, message, and rule in this plan's design artifacts traces to a spec FR/SC; no gameplay behavior is introduced that spec.md does not already define. |
| V. Scope Discipline (YAGNI) | PASS | No persistent storage, matchmaking, multi-map support, or spectator mode is included, consistent with spec Assumptions marking these out of scope. |
| Fair-Play & Security Requirements | PASS | Backend validates all inputs against spec-defined ranges (Data Model / contracts); win/loss/score/timer state is server-computed and logged per match for reproducibility; the 100ms latency budget is carried as an explicit Performance Goal/Constraint above. |

No violations — Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/001-multiplayer-gameplay-balance/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/
│   └── MatchServer/                # ASP.NET Core project
│       ├── Hubs/
│       │   └── MatchHub.cs          # SignalR Hub: connection lifecycle, SendInput, disconnect/forfeit handling (FR-020)
│       ├── Engine/                   # Pure, unit-testable classes: authoritative per-match game loop composed of
│       │   ├── MovementRules.cs       # movement/speed resolution (FR-001, FR-006, FR-009)
│       │   ├── CollisionRules.cs      # collision + simultaneity resolution (FR-021)
│       │   ├── VisionRules.cs         # fog-of-war + sonar pulse (FR-011)
│       │   ├── AntiCampingRules.cs    # anti-camping debuff (FR-012)
│       │   ├── ScoringRules.cs        # scoring matrix (FR-018)
│       │   └── WinConditionRules.cs   # win evaluation + tie-break (FR-017, FR-023)
│       ├── State/                    # POCOs mirroring data-model.md: MatchState, PlayerState, MapState, etc.
│       ├── Generated/                 # BalanceConstants.cs, code-generated from shared/balance-constants.json (not hand-edited)
│       └── Program.cs                 # ASP.NET Core / SignalR bootstrap
└── tests/
    ├── MatchServer.UnitTests/         # xUnit tests for Engine/*
    └── MatchServer.IntegrationTests/  # TestServer + SignalR.Client tests exercising full match flows (spec acceptance scenarios)

frontend/
├── src/
│   ├── net/                  # @microsoft/signalr client: connection, Hub method invocation, server-event handlers,
│   │                           exposed to React via a useMatchConnection hook
│   ├── render/                # Imperative Canvas draw loop for the live board only: map/tiles, Pac-Man/Ghost sprites,
│   │                           Frightened visual state (FR-008) — driven by state, not a React re-render per frame
│   ├── components/             # React components: JoinScreen, MatchBoard (mounts the canvas from render/), and HUD
│   │                           pieces — score, timer, lives, sonar pulse indicator, anti-camping speed-boost
│   │                           indicator (FR-013), results screen
│   ├── hooks/                   # useMatchConnection, useMatchState (subscribe to StateUpdate/events, expose as React state)
│   ├── generated/                # balanceConstants.ts, code-generated from shared/balance-constants.json (not hand-edited)
│   ├── styles/                    # Global theme: black page background + canvas clear color (see Visual Presentation below)
│   ├── App.tsx
│   └── main.tsx
└── tests/
    └── unit/                  # React Testing Library component tests (HUD/screens) + input-mapping and
                                 canvas-draw-loop unit tests (no gameplay logic lives here)

shared/
├── balance-constants.json   # Single, language-neutral source of truth for every spec-derived balance constant
│                              (speeds, durations, radii, point values, SC-006 latency target) — see research.md §5
└── codegen/                  # Small script (run in CI / pre-build) that generates
                                backend/src/MatchServer/Generated/BalanceConstants.cs and
                                frontend/src/generated/balanceConstants.ts from the JSON above

e2e/
├── tests/                    # Playwright specs, one per spec.md user story + the clarification-session edge
│                                cases — automates the scenarios in quickstart.md using two BrowserContexts
│                                (one per role) against the real backend + frontend
└── playwright.config.ts      # Launches/points at backend (dotnet run) and frontend (vite dev) before running
```

**Structure Decision**: Web application layout (`backend/` .NET solution + `frontend/` React/
TypeScript app + a language-neutral `shared/` constants source). `backend/` owns all gameplay logic
and authoritative state per Constitution Principle III; `frontend/` is a thin render/input layer
with no gameplay decision-making — React owns the application shell, screens, and HUD, while a
plain imperative Canvas draw loop (mounted inside one React-owned `<canvas>`) owns only the live
board's per-frame pixels, per `research.md` §3; `shared/balance-constants.json` plus its codegen
step exists solely to keep spec-derived numeric constants identical on both sides despite the
C#/TypeScript language split, directly supporting Principle I (balance changes must update one
place, not two — the generated files are build artifacts, never edited by hand). `e2e/` sits
outside both application packages since Playwright specs exercise the real backend and frontend
together as a black box, per `research.md` §4.

## Visual Presentation

Presentation-level decisions that are not gameplay rules (and therefore live here rather than in
`spec.md`, which stays implementation-agnostic):

- **Page background is black** (`#000`), applied globally in `frontend/src/styles/` — both the
  page/body background and the canvas clear color, so the maze area and the surrounding page read
  as one continuous black field with no seam at the canvas edge.
- This matches the classic Pac-Man presentation and gives maximum contrast for the elements the
  spec already requires to be legible: pellets, the flashing blue/white Frightened ghost (FR-008),
  the Hunter's sonar-pulse HUD indicator (FR-011), and the anti-camping speed-boost indicator
  (FR-013).
- HUD/screen text and chrome must therefore be light-on-dark; any component added later inherits
  the dark theme rather than defining its own background.

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*
