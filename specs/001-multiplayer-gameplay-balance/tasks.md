---

description: "Task list for 1v1 Asymmetric Multiplayer Gameplay & Balance Rules"
---

# Tasks: 1v1 Asymmetric Multiplayer Gameplay & Balance Rules

**Input**: Design documents from `/specs/001-multiplayer-gameplay-balance/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/match-room-protocol.md](./contracts/match-room-protocol.md), [quickstart.md](./quickstart.md)

**Tests**: Test tasks ARE included. The project constitution requires them (Principle II — every
simultaneity rule must have a test exercising it; Development Workflow & Quality Gates), and
`research.md` §4 defines the three-layer strategy this list follows: xUnit unit tests → SignalR
`TestServer` integration tests → Playwright end-to-end.

**Organization**: Tasks are grouped by user story so each story can be implemented, tested, and
demoed independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Web app layout per `plan.md`: `backend/src/MatchServer/` (.NET 10 / ASP.NET Core + SignalR),
`frontend/src/` (React + TypeScript + Vite), `shared/` (language-neutral balance constants),
`e2e/` (Playwright).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and toolchain

- [X] T001 Create the repository directory structure (`backend/`, `frontend/`, `shared/`, `e2e/`) exactly as laid out in plan.md "Source Code (repository root)"
- [X] T002 [P] Initialize the .NET 10 solution and ASP.NET Core project in backend/src/MatchServer/MatchServer.csproj with the SignalR package reference
- [X] T003 [P] Initialize the React + TypeScript + Vite application in frontend/ (package.json, vite.config.ts, tsconfig.json) with @microsoft/signalr as a dependency
- [X] T004 [P] Create shared/balance-constants.json containing every spec-derived constant listed in research.md §5 (pacmanBaseSpeed, ghostBaseSpeed, frightenedDurationMs, frightenedInversionMs, ghostSpeedFrightened, eyesSpeed, ghostHouseLockoutMs, visionRadiusTiles, sonarIntervalMs, campRadiusTiles, campTriggerMs, campSpeedPenalty, matchDurationMs, clearThresholdPct, FR-018 point values, maxInputLatencyMs)
- [X] T005 Create the codegen script in shared/codegen/generate.js that emits backend/src/MatchServer/Generated/BalanceConstants.cs and frontend/src/generated/balanceConstants.ts from shared/balance-constants.json (depends on T004)
- [X] T006 [P] Initialize the xUnit test projects backend/tests/MatchServer.UnitTests/MatchServer.UnitTests.csproj and backend/tests/MatchServer.IntegrationTests/MatchServer.IntegrationTests.csproj (the latter referencing Microsoft.AspNetCore.Mvc.Testing and Microsoft.AspNetCore.SignalR.Client)
- [X] T007 [P] Configure Vitest and React Testing Library in frontend/vitest.config.ts and frontend/tests/unit/setup.ts
- [X] T008 [P] Initialize the Playwright workspace in e2e/package.json and e2e/playwright.config.ts, with `webServer` entries that start the backend (`dotnet run`) and frontend (`vite dev`) before tests
- [X] T009 [P] Configure linting and formatting in .editorconfig (C#) and frontend/.eslintrc.cjs + frontend/.prettierrc (TypeScript/React)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The authoritative match skeleton — state objects, hub lifecycle, tick loop, and client
connection — that every user story builds on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T010 [P] Create the MatchState POCO in backend/src/MatchServer/State/MatchState.cs per data-model.md (matchId, status, elapsedMs, pacman, ghost, map, frightened, scoreChain, outcome)
- [X] T011 [P] Create the PlayerState POCO in backend/src/MatchServer/State/PlayerState.cs per data-model.md (connectionId, role, x, y, facing, speedMultiplier, livesRemaining, ghostSubState, respawnReadyAtMs, connected, score)
- [X] T012 [P] Create the MapState, Pellet, and PowerPellet POCOs in backend/src/MatchServer/State/MapState.cs per data-model.md, including totalPelletCount
- [X] T013 [P] Create the FrightenedState and Outcome types plus the Role, MatchStatus, GhostSubState, and ScoreEventType enums in backend/src/MatchServer/State/MatchEnums.cs and backend/src/MatchServer/State/FrightenedState.cs
- [X] T014 Define the single fixed map layout (walls, pellet and power-pellet positions, ghost house location) in backend/src/MatchServer/State/FixedMap.cs per FR-022
- [X] T015 Create the MatchManager in backend/src/MatchServer/Engine/MatchManager.cs holding all in-memory active matches keyed by match id, with create/lookup/dispose
- [X] T016 Create the MatchHub skeleton in backend/src/MatchServer/Hubs/MatchHub.cs implementing JoinMatch() with first-joiner-is-Runner role assignment and SignalR Group membership per contracts/match-room-protocol.md
- [X] T017 Implement the ~30Hz authoritative tick loop in backend/src/MatchServer/Engine/MatchLoopService.cs as a hosted service that advances every active match's elapsedMs and invokes the rule pipeline in a fixed, deterministic order (Constitution Principle II)
- [X] T018 Define MatchStateDto and the per-recipient projection seam in backend/src/MatchServer/Hubs/MatchStateDto.cs so each connection can receive its own filtered payload (filtering logic itself lands in US3)
- [X] T019 Implement the SendInput(direction) Hub method in backend/src/MatchServer/Hubs/MatchHub.cs with server-side validation that rejects and logs any value outside Up/Down/Left/Right/None rather than clamping it (Constitution Fair-Play requirement)
- [X] T020 Add structured per-match logging in backend/src/MatchServer/Engine/MatchLogger.cs recording every tick's authoritative decisions so outcomes are reproducible for dispute review (Constitution Fair-Play requirement)
- [X] T021 Wire the ASP.NET Core bootstrap in backend/src/MatchServer/Program.cs — SignalR service registration, the /hubs/match endpoint, CORS for the Vite dev origin, and MatchLoopService registration
- [X] T022 [P] Implement the SignalR client wrapper in frontend/src/net/matchConnection.ts (connect, invoke JoinMatch/SendInput, subscribe to StateUpdate/SonarPulse/ScoreEvent/MatchEnded)
- [X] T023 [P] Implement the useMatchConnection and useMatchState hooks in frontend/src/hooks/useMatchConnection.ts and frontend/src/hooks/useMatchState.ts, exposing the latest server state to React while holding the per-frame snapshot in a ref for the canvas loop
- [X] T024 [P] Create the React app shell in frontend/src/App.tsx and frontend/src/main.tsx with the JoinScreen component in frontend/src/components/JoinScreen.tsx
- [X] T025 [P] Create the global black theme in frontend/src/styles/theme.css — black (#000) page/body background and matching canvas clear color, light-on-dark text — per plan.md "Visual Presentation"

**Checkpoint**: Two clients can connect, be assigned roles, and receive ticking authoritative state — user story implementation can now begin

---

## Phase 3: User Story 1 - Core Asymmetric Match Loop (Priority: P1) 🎯 MVP

**Goal**: A complete, playable match — asymmetric speeds, 3 lives, ghost respawns, the 3-minute
timer, pellet collection, scoring, and every win/loss path resolving definitively

**Independent Test**: Run a match with only base speeds, lives, and the timer active (no power
pellets, no fog of war, no anti-camping) and confirm it always ends in a clear Pac-Man or Ghost
victory — quickstart.md scenario 1

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T026 [P] [US1] Unit tests for the 100%/95% speed differential and Pac-Man's zero-penalty cornering vs. the Ghost's cornering penalty — multiplicative 0.95 on current effective speed (0.95 × 0.95 = 0.9025 in normal state) lasting until the next tile center (FR-001) — in backend/tests/MatchServer.UnitTests/MovementRulesTests.cs
- [X] T027 [P] [US1] Unit tests for normal-state collision producing exactly one life loss and the 0.8x0.8 collision box (FR-002, FR-004) in backend/tests/MatchServer.UnitTests/CollisionRulesTests.cs
- [X] T028 [P] [US1] Unit tests for all win paths including the ≥70% threshold boundary and the FR-023 tie-break to Pac-Man (FR-015, FR-016, FR-017, FR-023) in backend/tests/MatchServer.UnitTests/WinConditionRulesTests.cs
- [X] T029 [P] [US1] Unit test asserting that when the match timer reaches 0:00 on the same tick Pac-Man collects the final pellet, the instant-clear victory (FR-015) takes precedence over timeout evaluation (FR-017), in backend/tests/MatchServer.UnitTests/WinConditionSimultaneityTests.cs
- [X] T030 [P] [US1] Unit tests for pellet +10, power pellet +50, elimination +500, and the +5/second time bonus (FR-018) in backend/tests/MatchServer.UnitTests/ScoringRulesTests.cs
- [X] T031 [P] [US1] Integration test driving a match to Ghost victory by lives depletion in backend/tests/MatchServer.IntegrationTests/LivesDepletedTests.cs
- [X] T032 [P] [US1] Integration test driving a match to Pac-Man victory by 100% pellet clear before timeout in backend/tests/MatchServer.IntegrationTests/PelletsClearedTests.cs
- [X] T033 [P] [US1] Integration test for the timer-expiry evaluation across the <70%, ≥70%-higher-score, and exact-tie cases in backend/tests/MatchServer.IntegrationTests/TimeoutEvaluationTests.cs
- [X] T034 [P] [US1] Integration test asserting a mid-match disconnect immediately forfeits to the remaining player with no grace period (FR-020) in backend/tests/MatchServer.IntegrationTests/DisconnectForfeitTests.cs
- [X] T035 [P] [US1] Playwright end-to-end spec covering quickstart.md scenario 1 with two BrowserContexts (one per role) in e2e/tests/core-match-loop.spec.ts

### Implementation for User Story 1

- [X] T036 [P] [US1] Implement MovementRules in backend/src/MatchServer/Engine/MovementRules.cs — grid movement, the 100%/95% base speeds, Pac-Man pre-buffered cornering, and the Ghost's off-center turn penalty (FR-001)
- [X] T037 [P] [US1] Implement ScoringRules in backend/src/MatchServer/Engine/ScoringRules.cs covering the FR-018 matrix rows reachable in this story (pellet, power pellet, elimination, time bonus)
- [X] T038 [US1] Implement normal-state collision detection and life decrement in backend/src/MatchServer/Engine/CollisionRules.cs (FR-002, FR-004)
- [X] T039 [US1] Implement WinConditionRules in backend/src/MatchServer/Engine/WinConditionRules.cs — instant clear, lives depleted, timeout evaluation against the 70% threshold, and the Pac-Man tie-break (FR-015, FR-016, FR-017, FR-023)
- [X] T040 [US1] Implement pellet collection and running clear-percentage tracking against totalPelletCount in backend/src/MatchServer/Engine/PelletRules.cs
- [X] T041 [US1] Implement Pac-Man life loss, respawn positioning, and the Ghost's 5-second post-elimination respawn delay in the tick pipeline at backend/src/MatchServer/Engine/MatchLoopService.cs (FR-002, FR-003)
- [X] T042 [US1] Implement the 180-second match countdown and its expiry hand-off to WinConditionRules in backend/src/MatchServer/Engine/MatchLoopService.cs (FR-014)
- [X] T043 [US1] Implement OnDisconnectedAsync forfeit resolution in backend/src/MatchServer/Hubs/MatchHub.cs (FR-020)
- [X] T044 [US1] Emit the MatchEnded event with winner, reason, and both final scores, then dispose the match and its group, in backend/src/MatchServer/Hubs/MatchHub.cs per contracts/match-room-protocol.md
- [X] T045 [US1] Emit ScoreEvent messages to both clients on every scoring action within the SC-005 1-second budget, in backend/src/MatchServer/Hubs/MatchHub.cs (FR-019)
- [X] T046 [P] [US1] Implement the canvas board draw loop (maze walls, pellets, power pellets) in frontend/src/render/boardRenderer.ts against the black clear color
- [X] T047 [P] [US1] Implement Pac-Man and Ghost sprite rendering with facing direction in frontend/src/render/spriteRenderer.ts
- [X] T048 [P] [US1] Build the MatchBoard component that mounts and sizes the canvas in frontend/src/components/MatchBoard.tsx
- [X] T049 [P] [US1] Build the HUD components for score, match timer, and lives in frontend/src/components/Hud.tsx
- [X] T050 [P] [US1] Build the results screen rendering MatchEnded winner and reason in frontend/src/components/ResultsScreen.tsx
- [X] T051 [US1] Implement keyboard input capture and direction-change forwarding to SendInput in frontend/src/hooks/useKeyboardInput.ts

**Checkpoint**: A full match is playable end-to-end by two browsers and always reaches a definitive outcome — MVP complete

---

## Phase 4: User Story 2 - Power Pellet Role Reversal (Priority: P2)

**Goal**: Power Pellets flip the hunt for 8 seconds — Ghost slowed to 70%, inputs inverted for 3
seconds, catchable for escalating points, then sent home on a lockout

**Independent Test**: Place a Power Pellet, consume it, and verify the speed drop, inversion
window, timer reset behavior, eaten/respawn sequence, and escalating bonuses — independent of
vision and anti-camping rules (quickstart.md scenario 2)

### Tests for User Story 2 ⚠️

- [X] T052 [P] [US2] Unit tests asserting an 8.0s Frightened window and that a second pickup resets rather than stacks the timer (FR-005) in backend/tests/MatchServer.UnitTests/FrightenedStateTests.cs
- [X] T053 [P] [US2] Unit tests for the 70% frightened speed and the 3.0s directional input inversion (FR-006, FR-007) in backend/tests/MatchServer.UnitTests/FrightenedMovementTests.cs
- [X] T054 [P] [US2] Unit tests for the 200/400/800/1600 chain progression and its reset between chains (FR-009) in backend/tests/MatchServer.UnitTests/ChainScoringTests.cs
- [X] T055 [P] [US2] Unit test for the FR-021 same-tick resolution order — elimination applies first, the Power Pellet is still consumed, and the Frightened window begins only after respawn — in backend/tests/MatchServer.UnitTests/SimultaneousCollisionTests.cs
- [X] T056 [P] [US2] Unit test asserting the Ghost cannot be caught again while already in EyesOnly or Respawning sub-state and must complete its return-and-lockout sequence before becoming a valid target, in backend/tests/MatchServer.UnitTests/GhostStateMachineTests.cs
- [X] T057 [P] [US2] Integration test walking the full Normal → Frightened → EyesOnly → Respawning → Normal ghost lifecycle in backend/tests/MatchServer.IntegrationTests/FrightenedLifecycleTests.cs
- [X] T058 [P] [US2] Playwright end-to-end spec covering quickstart.md scenario 2 in e2e/tests/frightened-state.spec.ts

### Implementation for User Story 2

- [X] T059 [US2] Implement Power Pellet consumption creating or resetting FrightenedState in backend/src/MatchServer/Engine/PelletRules.cs (FR-005)
- [X] T060 [US2] Apply the 70% frightened speed override in backend/src/MatchServer/Engine/MovementRules.cs (FR-006)
- [X] T061 [US2] Apply server-side directional input inversion during the first 3.0s of each Frightened window in backend/src/MatchServer/Engine/MovementRules.cs (FR-007)
- [X] T062 [US2] Implement the GhostSubState machine (Normal/Frightened/EyesOnly/Respawning) in backend/src/MatchServer/Engine/GhostStateMachine.cs per data-model.md transitions
- [X] T063 [US2] Implement eyes-only 150% return-to-ghost-house movement in backend/src/MatchServer/Engine/MovementRules.cs (FR-009)
- [X] T064 [US2] Implement the 5.0-second Ghost House lockout before re-release in backend/src/MatchServer/Engine/GhostStateMachine.cs (FR-009)
- [X] T065 [US2] Implement escalating chain scoring against MatchState.scoreChain in backend/src/MatchServer/Engine/ScoringRules.cs (FR-009)
- [X] T066 [US2] Implement the FR-021 fixed evaluation order (elimination before pickup) in backend/src/MatchServer/Engine/CollisionRules.cs
- [X] T067 [P] [US2] Render the flashing blue/white frightened ghost sprite in frontend/src/render/spriteRenderer.ts (FR-008)
- [X] T068 [P] [US2] Build the frightened blue-pulse overlay and countdown timer for the Ghost client in frontend/src/components/FrightenedOverlay.tsx (FR-008)

**Checkpoint**: Both User Story 1 and User Story 2 work independently

---

## Phase 5: User Story 3 - Vision Limits & Anti-Camping (Priority: P3)

**Goal**: The Ghost hunts with limited sight (radius + line-of-sight + periodic sonar) and is
penalized for camping uncollected Power Pellets

**Independent Test**: Position the Ghost at varying distances and behind walls to confirm
visibility and sonar timing; separately hold it near a Power Pellet past 5 seconds to confirm the
debuff applies and clears (quickstart.md scenario 3)

### Tests for User Story 3 ⚠️

- [X] T069 [P] [US3] Unit tests for the 6-tile radius and unobstructed line-of-sight visibility rules (FR-011) in backend/tests/MatchServer.UnitTests/VisionRulesTests.cs
- [X] T070 [P] [US3] Unit tests asserting the 4.0s sonar cadence resolves the correct quadrant and never exposes exact coordinates (FR-011) in backend/tests/MatchServer.UnitTests/SonarRulesTests.cs
- [X] T071 [P] [US3] Unit tests for the 3-tile/5.0s anti-camping trigger, the 15% penalty to 80% net speed, clearing on zone exit, and the camp-timer reset whenever Pac-Man becomes visible (FR-012), asserting the debuff becomes observable in the Ghost's state within 1.0 second of the threshold being crossed (SC-004), in backend/tests/MatchServer.UnitTests/AntiCampingRulesTests.cs
- [X] T072 [P] [US3] Integration test asserting the Hunter's connection never receives the Runner's true position on ticks where the Runner is not visible, and that the Runner is never fog-restricted (FR-010, FR-011) in backend/tests/MatchServer.IntegrationTests/FogOfWarFilteringTests.cs
- [X] T073 [P] [US3] Playwright end-to-end spec covering quickstart.md scenario 3 in e2e/tests/vision-and-camping.spec.ts

### Implementation for User Story 3

- [X] T074 [P] [US3] Implement VisionRules in backend/src/MatchServer/Engine/VisionRules.cs — 6-tile radius plus corridor line-of-sight resolution (FR-011)
- [X] T075 [P] [US3] Implement AntiCampingRules in backend/src/MatchServer/Engine/AntiCampingRules.cs — per-power-pellet camp timers that reset to zero whenever Pac-Man is visible to the Ghost per FR-011, the 15% debuff, and clearing on exit or collection (FR-012)
- [X] T076 [US3] Implement per-recipient state filtering in backend/src/MatchServer/Hubs/MatchStateDto.cs so the Hunter's payload omits the Runner's position whenever VisionRules says it is not visible, while the Runner always receives full visibility (FR-010, FR-011, Constitution Principle III)
- [X] T077 [US3] Emit SonarPulse quadrant messages to the Hunter connection every 4.0s while the Runner is outside direct visibility, resolving the quadrant map-relative (NE/NW/SE/SW split at the map's midlines, never Hunter-relative), in backend/src/MatchServer/Hubs/MatchHub.cs (FR-011)
- [X] T078 [US3] Apply the anti-camping speed debuff to the Ghost's effective speed in backend/src/MatchServer/Engine/MovementRules.cs (FR-012)
- [X] T079 [P] [US3] Render the Hunter's fog-of-war view — draw only what the filtered state contains — in frontend/src/render/boardRenderer.ts
- [X] T080 [P] [US3] Build the sonar pulse HUD indicator in frontend/src/components/SonarIndicator.tsx (FR-011)
- [X] T081 [P] [US3] Build the Pac-Man speed-boost indicator shown while the anti-camping debuff is active in frontend/src/components/SpeedBoostIndicator.tsx (FR-013)

**Checkpoint**: All three user stories are independently functional

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verification against measurable success criteria, plus cleanup

- [X] T082 [P] Playwright end-to-end spec covering the clarification-session edge cases from quickstart.md scenario 4 (70% boundary, exact tie, FR-021 simultaneity, disconnect forfeit, and the timer-expiry vs. final-pellet race), plus an assertion that a ScoreEvent is reflected in both clients' HUDs within 1.0 second with identical values (SC-005), in e2e/tests/edge-cases.spec.ts
- [X] T083 Measure round-trip input-to-effect latency against the ≤100ms budget and record the result, adding the instrumentation in backend/src/MatchServer/Engine/MatchLogger.cs (SC-006)
- [ ] T084 Run playtest matches between similarly-skilled players and record the role win-rate split against the ~60/40 balance target, documenting findings in specs/001-multiplayer-gameplay-balance/playtest-results.md (SC-003)
- [X] T085 [P] Wire shared/codegen/generate.js into the backend and frontend pre-build steps so generated constants can never go stale (Constitution Principle I)
- [X] T086 [P] Add frontend component tests for the HUD, sonar indicator, and speed-boost indicator in frontend/tests/unit/components.test.tsx
- [X] T087 [P] Add frontend unit tests for input mapping and canvas draw-loop state selection in frontend/tests/unit/render.test.ts
- [X] T088 Review all Hub input paths for reject-don't-clamp validation coverage in backend/src/MatchServer/Hubs/MatchHub.cs (Constitution Fair-Play requirement)
- [X] T089 [P] Write the developer README covering setup, codegen, and the three test layers in README.md
- [X] T090 Run the full quickstart.md validation guide manually and confirm every scenario matches its spec citation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **User Stories (Phase 3–5)**: All depend on Foundational completion; can then proceed in parallel (if staffed) or sequentially in priority order (P1 → P2 → P3)
- **Polish (Phase 6)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Depends only on Foundational — no dependencies on other stories
- **User Story 2 (P2)**: Depends only on Foundational. Shares `MovementRules.cs`, `ScoringRules.cs`, `CollisionRules.cs`, and `PelletRules.cs` files with US1, so if both are worked simultaneously coordinate edits to those four files; the *behavior* remains independently testable
- **User Story 3 (P3)**: Depends only on Foundational. Shares `MovementRules.cs` (debuff application) and `boardRenderer.ts` (fog view) with earlier stories — same coordination note

### Within Each User Story

- Tests MUST be written and failing before implementation
- State/POCOs before rules; rules before hub wiring; backend before the frontend that renders it
- Story complete and checkpoint-validated before moving to the next priority

### Parallel Opportunities

- Setup: T002, T003, T004, T006, T007, T008, T009 all run in parallel (T005 waits on T004)
- Foundational: all four state POCO tasks (T010–T013) in parallel; frontend scaffolding (T022–T025) in parallel with backend T014–T021
- US1: all ten test tasks (T026–T035) in parallel; then T036/T037 in parallel, and all five frontend tasks (T046–T050) in parallel
- US2: all seven test tasks (T052–T058) in parallel; frontend T067/T068 in parallel
- US3: all five test tasks (T069–T073) in parallel; frontend T079–T081 in parallel
- Across stories: once Phase 2 is done, three developers can take US1, US2, and US3 concurrently

---

## Parallel Example: User Story 1

```bash
# Launch all User Story 1 tests together (they must fail first):
Task: "Unit tests for speed differential in backend/tests/MatchServer.UnitTests/MovementRulesTests.cs"
Task: "Unit tests for normal-state collision in backend/tests/MatchServer.UnitTests/CollisionRulesTests.cs"
Task: "Unit tests for win paths in backend/tests/MatchServer.UnitTests/WinConditionRulesTests.cs"
Task: "Unit tests for the scoring matrix in backend/tests/MatchServer.UnitTests/ScoringRulesTests.cs"
Task: "Playwright core match loop spec in e2e/tests/core-match-loop.spec.ts"

# Then launch all User Story 1 frontend work together:
Task: "Canvas board draw loop in frontend/src/render/boardRenderer.ts"
Task: "Sprite rendering in frontend/src/render/spriteRenderer.ts"
Task: "MatchBoard component in frontend/src/components/MatchBoard.tsx"
Task: "HUD components in frontend/src/components/Hud.tsx"
Task: "Results screen in frontend/src/components/ResultsScreen.tsx"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run `npx playwright test e2e/tests/core-match-loop.spec.ts` plus the US1 unit and integration suites
5. Deploy/demo — a complete, fair, playable 1v1 match without power pellets or fog of war

### Incremental Delivery

1. Setup + Foundational → two clients connect and receive authoritative state
2. Add US1 → definitive match outcomes → **MVP demo**
3. Add US2 → power pellet counter-play and tension swings → demo
4. Add US3 → vision limits and anti-camping close the degenerate-strategy gaps → demo
5. Phase 6 → verify SC-003 balance and SC-006 latency against real measurements

### Parallel Team Strategy

1. Team completes Setup + Foundational together
2. Then: Developer A on US1, Developer B on US2, Developer C on US3
3. Coordinate on the shared rule files noted under User Story Dependencies

---

## Notes

- [P] tasks = different files, no dependencies
- Every task traces to a spec FR/SC or a constitution requirement — no gameplay behavior appears here that spec.md does not define (Constitution Principle IV)
- Balance constants are never hardcoded in a task's implementation; they come from the generated constants (Constitution Principle I)
- Verify tests fail before implementing
- Commit after each task or logical group; stop at any checkpoint to validate a story independently
