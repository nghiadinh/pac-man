# Playtest Results — Balance Validation

**Feature**: [spec.md](./spec.md) | **Criterion**: SC-003 (neither role wins substantially more
often than the other; target within roughly a 60/40 split)

## Status: NOT YET MEASURED

SC-003 is the one success criterion that **cannot be satisfied by automated testing**. It asks
whether two similarly-skilled humans find the match fair, which is a question about people, not
about code. The rules are verified — the win-rate they produce is not.

Everything else has been verified:

| Criterion | Method | Status |
|---|---|---|
| SC-001 definitive outcome | Unit + integration across all four end conditions | ✅ Verified |
| SC-002 tail-chase never wins | `MovementRulesTests` speed differential | ✅ Verified |
| **SC-003 balance (~60/40)** | **Human playtesting** | ⏳ **Requires humans** |
| SC-004 debuff within 1s | `AntiCampingRulesTests` | ✅ Verified |
| SC-005 score sync within 1s | Playwright, two real browsers | ✅ Verified |
| SC-006 ≤100ms input-to-effect | `MatchLogger.TickLatency` instrumentation | ⚠️ Instrumented, not yet measured under real network conditions |

## Prerequisites before playtesting

**Do not gather balance data until all three user stories are complete.** A match missing fog of
war (US3) or the Power Pellet reversal (US2) is a materially different game, and win rates from it
say nothing about the shipped ruleset. All three are now implemented, so this precondition is met.

## Protocol

1. Recruit at least 4 players of comparable skill; unequal skill measures the players, not the rules.
2. Play a minimum of 20 matches, **swapping roles between every match** so an individual's
   preference for one side cancels out.
3. Record per match: winner, end reason, final scores, and percentage of pellets cleared.
4. Compute the overall win split by ROLE, not by player.

## Recording table

| # | Pac-Man player | Ghost player | Winner | Reason | Pac-Man score | Ghost score | Cleared % |
|---|---|---|---|---|---|---|---|
| 1 | | | | | | | |
| 2 | | | | | | | |

*(extend as matches are played)*

## Results

**Matches played**: 0
**Pac-Man wins**: — **Ghost wins**: — **Split**: —

## Interpretation guide

- **Within 60/40** — SC-003 is satisfied; no balance change is warranted.
- **Outside 60/40** — the ruleset needs adjustment. Per Constitution Principle I, any change edits
  `shared/balance-constants.json` **and** the owning requirement in `spec.md` in the same change,
  citing the measured split as the justification.

Most likely levers, in rough order of impact:

| Symptom | Lever | Requirement |
|---|---|---|
| Ghost wins too often | Widen the base speed gap (`ghostBaseSpeed` below 0.95) | FR-001 |
| Ghost wins too often | Lengthen the Frightened window | FR-005 |
| Pac-Man wins too often | Narrow the speed gap, or shorten the Frightened window | FR-001 / FR-005 |
| Pac-Man wins too often | Widen the Ghost's vision radius | FR-011 |
| Timeouts dominate | Adjust the 70% clear threshold | FR-017 |

Change one lever at a time and re-measure. Changing several at once makes the next 20 matches
uninterpretable.
