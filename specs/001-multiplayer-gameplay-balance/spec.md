# Feature Specification: 1v1 Asymmetric Multiplayer Gameplay & Balance Rules

**Feature Branch**: `001-multiplayer-gameplay-balance`

**Created**: 2026-08-14

**Status**: Draft

**Input**: User description: "Source design document `specs/specs_multiplayer-gameplay.md` — 1v1 Asymmetric Multiplayer Gameplay & Balance Rules for a web-based Pac-Man game. Two human players compete as Pac-Man (Runner) versus a human-controlled Ghost (Hunter). The document defines asymmetric movement speeds, a Power Pellet 'Frightened State' role reversal, vision/fog-of-war rules for the Ghost, an anti-camping penalty, a 3-minute match timer with win/loss conditions, and a scoring matrix."

## Clarifications

### Session 2026-08-14

- Q: What is the maximum network delay between a player's input and it taking effect in the match before the game is considered unfairly imprecise? → A: ≤100ms round-trip (typical competitive-online-game standard)
- Q: If a player disconnects mid-match, what should happen to the match? → A: The remaining connected player is immediately awarded a forfeit win
- Q: If Pac-Man eats a Power Pellet in the same instant the Ghost makes contact with Pac-Man in normal state, which rule applies? → A: Elimination takes precedence — Pac-Man loses a life; the Power Pellet is still consumed but Frightened State starts fresh on respawn
- Q: Does the MVP need to support multiple/selectable maps, or a single fixed map? → A: Single fixed map for this feature; multi-map support is out of scope
- Q: What determines the "Ghost benchmark" score used in the ≥70%-cleared timeout evaluation, and how are exact ties broken? → A: Ghost's benchmark is its own accumulated score under the same scoring matrix (primarily +500 per Pac-Man elimination); an exact tie is resolved in Pac-Man's favor

### Session 2026-08-14 (post-analysis remediation)

Resolved by `/speckit-analyze` findings U1, U2, and A1 — three values that were otherwise
undefined and would have been decided arbitrarily during implementation:

- Q: What is the origin of the sonar pulse's "approximate quadrant" — the map or the Ghost's position? → A: Map-relative (NE/NW/SE/SW split at the map's horizontal and vertical midlines); Ghost-relative would amount to a bearing-to-target and would erode the fog-of-war disadvantage FR-011 exists to create
- Q: What counts as "actively chasing Pac-Man" for the purpose of suppressing the anti-camping penalty? → A: Pac-Man being currently visible to the Ghost per FR-011; the camp timer resets to zero on entering visibility and resumes from zero on leaving it
- Q: Is the Ghost's 5% cornering penalty multiplicative or percentage-point, and how long does it last? → A: Multiplicative on the Ghost's current effective speed (so it composes with Frightened/Anti-Camping modifiers), lasting until the Ghost reaches the next tile center

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Core Asymmetric Match Loop (Priority: P1)

Two players start a match: one controls Pac-Man (the Runner), the other controls a Ghost (the Hunter). Pac-Man moves at full speed and must clear all pellets on the map before the 3-minute timer expires, or survive with at least one life remaining. The Ghost moves slightly slower than Pac-Man and must eliminate Pac-Man three times, relying on prediction and positioning rather than a direct chase, since a straight tail-chase can never succeed at a speed disadvantage.

**Why this priority**: This is the foundational match experience. Without asymmetric speed, lives/respawns, and a resolvable win condition, there is no playable game — every other mechanic (power pellets, vision, anti-camping) is a refinement layered on top of this core loop.

**Independent Test**: Can be fully tested by running a match with only base speed differentials, lives, and the 3-minute timer active (no power pellets, no fog of war, no anti-camping) and confirming the match always ends in a clear Pac-Man or Ghost victory.

**Acceptance Scenarios**:

1. **Given** a match has started with Pac-Man at 3 lives and the Ghost with unlimited respawns, **When** the Ghost makes contact with Pac-Man in the normal (non-frightened) state, **Then** Pac-Man loses one life and, if lives reach 0, the Ghost immediately wins the match.
2. **Given** a match is in progress, **When** Pac-Man collects the final remaining pellet on the map before the timer reaches 0:00, **Then** Pac-Man immediately wins the match regardless of remaining time.
3. **Given** the Ghost is actively chasing Pac-Man in a straight corridor with no turns, **When** both players move optimally, **Then** the Ghost never closes the distance to Pac-Man, because the Ghost's base speed (95%) is permanently lower than Pac-Man's (100%).
4. **Given** the match timer reaches 0:00, **When** Pac-Man has cleared at least 70% of the map's pellets and has a score equal to or higher than the Ghost's, **Then** Pac-Man wins; **When** Pac-Man has cleared less than 70% of the pellets, **Then** the Ghost wins.

---

### User Story 2 - Power Pellet Role Reversal (Frightened State) (Priority: P2)

When Pac-Man eats a Power Pellet, the tables turn for 8 seconds: the Ghost is slowed down, has its controls inverted for the first 3 seconds, and becomes vulnerable — if Pac-Man catches the Ghost during this window, the Ghost is sent back to its home base and Pac-Man scores escalating bonus points for consecutive catches.

**Why this priority**: This mechanic gives Pac-Man an active counter-play tool and creates the game's signature tension swings. It builds directly on the core loop from User Story 1 and is not required for a match to be playable, but is essential to the intended competitive balance and is called out as a primary system in the source design.

**Independent Test**: Can be fully tested by placing a Power Pellet on a test map, having Pac-Man consume it, and verifying the Ghost's speed drop, control inversion window, visual state change, eaten/respawn sequence, and the escalating point bonuses — independent of vision or anti-camping rules.

**Acceptance Scenarios**:

1. **Given** Pac-Man eats a Power Pellet, **When** the Frightened State begins, **Then** the Ghost's speed drops from 95% to 70% of base grid speed for 8.0 seconds, and its sprite/UI switch to the flashing "frightened" visual state.
2. **Given** the Frightened State has just begun, **When** the Ghost player presses a directional input during the first 3.0 seconds, **Then** the input is inverted (Up↔Down, Left↔Right).
3. **Given** the Frightened State is active with 2 seconds remaining, **When** Pac-Man eats a second Power Pellet, **Then** the Frightened State timer resets to a fresh 8.0 seconds rather than stacking.
4. **Given** the Ghost is in Frightened State, **When** Pac-Man makes contact with the Ghost, **Then** the Ghost switches to "Eyes Only" mode, travels at 150% speed back to the Ghost House, is locked out for 5.0 seconds before re-entering play, and Pac-Man is awarded the next value in the 200/400/800/1600-point escalating sequence (resetting to 200 the next time a new chain starts).

---

### User Story 3 - Vision Limits & Anti-Camping Enforcement (Priority: P3)

The Ghost does not see the full map the way Pac-Man does — it only sees a radius around itself, down clear corridors, and gets a periodic "sonar" hint of Pac-Man's general area when it can't see him directly. Separately, if the Ghost player tries to sit near an uncollected Power Pellet to guard it instead of actively hunting, the Ghost is penalized with extra slowdown until it moves away.

**Why this priority**: These rules prevent degenerate, unfun strategies (omniscient hunting and static camping) that would undermine the balance established by User Stories 1 and 2. They matter for long-term competitive health but a match is technically playable without them, making this the lowest-priority independently-shippable slice.

**Independent Test**: Can be fully tested by placing the Ghost at varying distances/behind walls from Pac-Man and confirming visibility rules and sonar pulse timing; separately, by holding the Ghost stationary near an uncollected Power Pellet for over 5 seconds and confirming the speed debuff applies and clears correctly.

**Acceptance Scenarios**:

1. **Given** Pac-Man is within 6 tiles of the Ghost or in an unobstructed line of sight down a corridor, **When** the Ghost player looks at their screen, **Then** Pac-Man is visible.
2. **Given** Pac-Man is outside the 6-tile radius and not in a direct line of sight, **When** 4.0 seconds elapse, **Then** the Ghost's HUD emits a sonar pulse indicating Pac-Man's approximate quadrant, without revealing Pac-Man's exact position.
3. **Given** the Ghost remains within 3 tiles of an uncollected Power Pellet for more than 5.0 continuous seconds without actively chasing Pac-Man, **When** the anti-camping threshold is crossed, **Then** the Ghost's speed is reduced by an additional 15% (to 80% of base) and Pac-Man's HUD shows a speed-boost indicator, until the Ghost leaves the 3-tile zone.
4. **Given** Pac-Man has full map visibility at all times, **When** a match is running, **Then** no fog-of-war, radius, or sonar restriction is ever applied to Pac-Man's view.

---

### Edge Cases

- What happens if the match timer reaches 0:00 in the same instant Pac-Man collects the final pellet? Pac-Man's instant-clear victory (User Story 1, Scenario 2) takes precedence over the timeout evaluation.
- What happens if the Ghost is eaten while already in "Eyes Only" mode from a prior catch (e.g., overlapping Frightened windows)? The Ghost cannot be eaten again while already in "Eyes Only" mode; it must complete its return-and-lockout sequence before it is a valid target again.
- What happens if the Ghost is inside the Frightened control-inversion window (first 3.0 seconds) exactly when the Frightened State ends? Inversion always resolves within the first 3.0 seconds of an 8.0-second window, so it cannot outlast the Frightened State itself.
- How does the system handle the anti-camping debuff if the last uncollected Power Pellet on the map is eaten while the Ghost is mid-penalty? The debuff clears immediately, since the zone trigger requires an "uncollected" Power Pellet to exist.
- What happens if Pac-Man has exactly 70% of pellets cleared (the boundary value) at timeout? 70% meets the "≥70%" threshold, so the match proceeds to the score comparison rather than an automatic Ghost win.
- What happens if Pac-Man's and Ghost's scores are exactly tied at the 70%+ timeout evaluation? Ties are resolved in Pac-Man's favor (FR-023), consistent with Pac-Man having already met the primary 70%-clear bar.
- What happens if a player disconnects mid-match? The match ends immediately and the remaining connected player is awarded a forfeit win (FR-020).
- What happens if Pac-Man eats a Power Pellet in the exact same instant the Ghost makes normal-state contact with Pac-Man? Elimination takes precedence: Pac-Man loses a life (FR-021); the Power Pellet is still consumed, and Frightened State begins fresh once Pac-Man respawns rather than retroactively saving the life that was lost.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST move Pac-Man at 100% base grid speed and the Ghost at 95% base grid speed during normal (non-frightened) play, with no cornering speed loss for Pac-Man and a cornering penalty for the Ghost when turning off-grid-center: its effective speed is multiplied by 0.95 for the remainder of its traversal to the next tile center, after which it returns to its current state's normal speed. The penalty is multiplicative on the Ghost's *current* effective speed, so it composes with the Frightened (FR-006) and Anti-Camping (FR-012) modifiers rather than replacing them — e.g. a normal-state Ghost corners at 0.95 × 0.95 = 0.9025.
- **FR-002**: System MUST give Pac-Man 3 lives, ending the match in a Ghost victory the instant lives reach 0.
- **FR-003**: System MUST give the Ghost unlimited respawns, with a 5-second delay after being eliminated in normal-state contact.
- **FR-004**: System MUST use matching 0.8×0.8 tile collision boxes for both Pac-Man and the Ghost.
- **FR-005**: System MUST trigger an 8.0-second Frightened State whenever Pac-Man consumes a Power Pellet, and MUST reset (not stack) the timer to a fresh 8.0 seconds if another Power Pellet is consumed while already active.
- **FR-006**: System MUST reduce the Ghost's movement speed to 70% of base grid speed for the full duration of the Frightened State.
- **FR-007**: System MUST invert the Ghost player's directional inputs (Up↔Down, Left↔Right) for the first 3.0 seconds of each Frightened State.
- **FR-008**: System MUST visually distinguish the Frightened State via a flashing blue/white Ghost sprite and a blue pulse/countdown overlay on the Ghost's UI.
- **FR-009**: System MUST, upon Pac-Man contacting a Frightened Ghost, award Pac-Man escalating points (200 / 400 / 800 / 1600) per consecutive catch within an unbroken chain, switch the Ghost to "Eyes Only" mode moving at 150% speed toward the Ghost House, and enforce a 5.0-second lockout at the Ghost House before re-release.
- **FR-010**: System MUST give Pac-Man full, unrestricted visibility of the entire map at all times.
- **FR-011**: System MUST restrict the Ghost's visibility to a 6-tile direct radius plus unobstructed line-of-sight corridors, and MUST emit a sonar pulse on the Ghost's HUD every 4.0 seconds indicating Pac-Man's approximate quadrant whenever Pac-Man is outside that visible area. A quadrant is one of four fixed regions of the map (NE/NW/SE/SW), determined by the map's horizontal and vertical midlines — it is map-relative, not Ghost-relative, and therefore conveys no bearing or distance to the Ghost's own position.
- **FR-012**: System MUST apply an Anti-Camping Debuff (additional 15% speed reduction, net 80% of base) to the Ghost whenever it remains within a 3-tile radius of an uncollected Power Pellet for more than 5.0 continuous seconds while Pac-Man is not visible to the Ghost (per FR-011), and MUST clear the debuff once the Ghost exits that zone. The camp timer resets to zero whenever Pac-Man enters the Ghost's visible area, and resumes from zero when Pac-Man leaves it.
- **FR-013**: System MUST display a Pac-Man speed-boost indicator on the HUD for the duration of an active Anti-Camping Debuff.
- **FR-014**: System MUST run each match on a strict 180-second (3:00) countdown timer.
- **FR-015**: System MUST declare Pac-Man the immediate winner if 100% of pellets are collected before the timer expires.
- **FR-016**: System MUST declare the Ghost the immediate winner if Pac-Man's life count reaches 0 via normal-state contact.
- **FR-017**: System MUST, when the timer reaches 0:00, declare the Ghost the winner if Pac-Man has collected less than 70% of total pellets, and otherwise compare scores per FR-018 to determine the winner.
- **FR-018**: System MUST track Pac-Man's and the Ghost's scores in real time per the scoring matrix (regular pellet +10, power pellet +50, chained ghost catches +200/+400/+800/+1600, Ghost eliminating Pac-Man +500, and a +5-per-second-remaining time bonus to Pac-Man if 100% of pellets are cleared) and use these accumulated totals as the "score" compared under FR-017.
- **FR-019**: System MUST display synchronized score updates to both players in real time as scoring events occur.
- **FR-020**: System MUST immediately end the match and award a forfeit victory to the remaining connected player if the other player disconnects during a match.
- **FR-021**: System MUST resolve a same-instant collision between a normal-state Ghost-Pac-Man contact and a Power Pellet pickup by applying the elimination (life loss) first; the Power Pellet is still consumed, and any resulting Frightened State begins fresh only once Pac-Man has respawned.
- **FR-022**: System MUST run all matches on a single fixed map; multi-map or map-selection support is out of scope for this feature.
- **FR-023**: System MUST break an exact score tie during the ≥70%-cleared timeout evaluation (FR-017) in Pac-Man's favor.
- **FR-024**: System MUST pair two players into the same match when both request one without naming a room, and MUST guarantee this holds even when both requests arrive simultaneously — two players requesting a match at the same instant MUST NOT end up waiting alone in separate matches.
- **FR-025**: System MUST allow two players to agree a short room code in advance and join that specific match, so that a player can choose their opponent rather than being paired with whoever requests a match first.
- **FR-026**: System MUST keep a match opened with a chosen room code private — it MUST NOT be offered to players who did not name that code, so the slot stays available for the intended opponent.
- **FR-027**: System MUST refuse, with a distinguishable explanation, a request to join a room that is invalid or already has two players, and MUST NOT reroute that player into a different match instead.
- **FR-028**: System MUST show each waiting player their room code so it can be shared with a chosen opponent, including for matches created by automatic pairing.

### Key Entities

- **Match**: A single timed contest (180 seconds) between one Pac-Man player and one Ghost player on a fixed map, ending in a Pac-Man Victory or Ghost Victory outcome.
- **Pac-Man (Runner)**: The player-controlled role with full map visibility, 3 lives, no cornering penalty, and the objective of clearing all pellets or surviving to a favorable timeout.
- **Ghost (Hunter)**: The player-controlled role with restricted vision, unlimited respawns, a base speed disadvantage, and the objective of eliminating Pac-Man three times.
- **Pellet**: A regular collectible worth 10 points; contributes to the 70%/100% map-clear thresholds.
- **Power Pellet**: A collectible worth 50 points that triggers the Frightened State when eaten by Pac-Man.
- **Frightened State**: A temporary (8.0s) match phase during which the Ghost is slowed, has inputs inverted for 3.0s, and is vulnerable to being caught by Pac-Man.
- **Score Event**: A discrete point-earning action (pellet collected, power pellet collected, Ghost caught in a chain, Pac-Man eliminated, time bonus) attributed to one player.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every match reaches a definitive Pac-Man or Ghost victory outcome — no match ends undecided.
- **SC-002**: A Ghost player using only a direct, no-prediction tail-chase never wins a match, since their sustained speed never exceeds 95% of Pac-Man's during normal play.
- **SC-003**: Across matches between similarly-skilled players, neither role wins substantially more often than the other (target: within roughly a 60/40 split), indicating the asymmetric rules produce a competitively balanced game.
- **SC-004**: Ghost players who idle near an uncollected Power Pellet for more than 5 seconds are visibly and measurably slowed within 1 second of crossing the threshold, in 100% of such occurrences.
- **SC-005**: Both players can see their live score update within 1 second of any scoring event, with no perceptible desync between the two players' views of the score.
- **SC-006**: Round-trip network delay between a player's input and its effect in the match stays at or below 100ms, so the Ghost's speed disadvantage remains consistently perceptible and fair rather than being masked by network jitter.

## Assumptions

- The scope of this specification is limited to gameplay balance rules (speed, vision, power-pellet effects, anti-camping, win conditions, scoring) for a single match on a single fixed map (confirmed, FR-022); netcode/synchronization implementation and map design/layout are out of scope.
- **Room joining (FR-024–FR-028) was added on 2026-08-15**, after the original "matchmaking is out of scope" assumption proved unworkable in practice: with three or more people connected there was no way to choose an opponent, and simultaneous joins could strand both players in separate empty matches. It covers pairing and room codes only — ranked matchmaking, skill rating, lobbies, and persistent player identity remain out of scope.
- Role selection beyond first-joiner-is-Pac-Man is out of scope; players who want a particular side swap by taking turns joining first.
- A "match" is a single 3-minute round; no best-of-series or automatic role-swap between rounds is defined by this specification. If side-swapping for fairness across multiple rounds is desired, it will be addressed in a separate specification.
- Both players have a real-time connection with round-trip latency at or below 100ms (see SC-006), sufficient that the defined speed differentials (e.g., 95% vs. 100%) remain perceptually accurate; detailed network synchronization implementation is handled by existing underlying multiplayer infrastructure and is not redefined here.
- The underlying movement model is grid-based (classic Pac-Man style), consistent with the source document's references to "grid unit speed."
