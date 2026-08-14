<!--
Sync Impact Report
- Version change: (unratified template) → 1.0.0
- Modified principles: n/a (initial ratification — all five principle slots filled for the first time)
- Added sections:
  - Core Principles: I. Competitive Fairness by Design, II. Deterministic Rule Resolution,
    III. Server-Authoritative State, IV. Spec-First Development, V. Scope Discipline (YAGNI)
  - Fair-Play & Security Requirements (Section 2)
  - Development Workflow & Quality Gates (Section 3)
  - Governance
- Removed sections: none
- Templates requiring follow-up: none — plan/spec/tasks templates already reference "Constitution
  Check" gates generically and need no structural change for this ratification.
- Deferred items:
  - TODO(RATIFICATION_DATE): No prior constitution or repo history exists; ratification date is set
    to the date this document was first authored. Confirm/correct if an earlier informal agreement
    predates this file.
-->

# Pac-Man 1v1 Multiplayer Constitution

## Core Principles

### I. Competitive Fairness by Design

Every asymmetric mechanic (speed differentials, vision limits, timers, penalties, scoring) MUST be
expressed as an explicit, quantified rule in an approved spec before it is implemented — never as
an informal tuning decision made only in code or in a pull request description. Changes to
balance-affecting constants (speed percentages, durations, thresholds, point values) MUST cite the
measurable success criteria they are intended to satisfy (e.g., a target win-rate split between
roles) and MUST update the owning spec's Success Criteria alongside the code change.

Rationale: This project's core value proposition is a fair, competitive 1v1 experience between two
structurally different roles (Runner vs. Hunter). If balance changes can land without being tied
back to a measurable, spec-level target, the game drifts out of balance silently and there is no
way to tell a deliberate design change from an unintended regression.

### II. Deterministic Rule Resolution

Every rule, and especially every conflict between two rules that can occur in the same instant
(e.g., an elimination and a power-up pickup happening simultaneously), MUST have exactly one
documented, deterministic outcome. "Whatever the client/engine happens to do" is not an acceptable
resolution. Ambiguous simultaneity cases MUST be resolved during specification (via
`/speckit-clarify` or explicit spec authoring) before implementation begins, not left to be decided
implicitly by code ordering or engine internals.

Rationale: In a real-time competitive game, non-deterministic edge-case handling is both a fairness
problem (identical situations resolving differently for different players) and a bug-report
magnet. Deciding these cases at the spec level, ahead of implementation, keeps them testable and
reviewable.

### III. Server-Authoritative State (NON-NEGOTIABLE)

All gameplay-affecting state — position, speed, collisions, timers, scores, and win/loss
determination — MUST be computed and validated on a server (or equivalent trusted authority), never
trusted from an unverified client. Clients render state and submit inputs; they MUST NOT be treated
as the source of truth for anything that affects match outcome or score.

Rationale: The entire balance model depends on precise, small margins (e.g., a five-percent speed
differential, a fixed 3-tile camping radius). A client with the ability to misreport its own state
can trivially erase those margins, which would make every other principle in this document
unenforceable. This is treated as non-negotiable rather than a default that can be waived per
feature.

### IV. Spec-First Development

No gameplay behavior is implemented before it exists as an approved spec with testable acceptance
scenarios (via the `/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` →
`/speckit-implement` flow). Implementation tasks MUST trace back to specific functional requirements
or acceptance scenarios in a spec; code changes that alter observable gameplay behavior without a
corresponding spec update are out of process and MUST be reverted or retroactively specified before
merge.

Rationale: This project is managed with Spec Kit specifically so that game-balance and competitive
rules are captured, reviewed, and versioned as text before they become code. Skipping the spec step
for "small" gameplay tweaks is exactly how undocumented, unbalanced behavior accumulates.

### V. Scope Discipline (YAGNI)

Features and abstractions are built for the match structure and scope defined in the current,
approved spec — not for hypothetical future modes, maps, or player counts. Generalizing a mechanic
(e.g., building multi-map support, spectator modes, or ranked matchmaking) before a spec calls for
it is out of scope and MUST wait for its own specification.

Rationale: An asymmetric 1v1 balance system is already delicate; speculative generality (extra
configuration knobs, abstracted-but-unused code paths) multiplies the surface area that has to stay
balanced and correct for no current benefit.

## Fair-Play & Security Requirements

- All win/loss, score, and timer determinations MUST be made server-side and MUST be
  reproducible from logged match state for post-match dispute review.
- Any input or state value influencing movement speed, collision, or scoring MUST be validated
  server-side against the ranges defined in the owning spec; out-of-range values MUST be rejected,
  not clamped silently into gameplay.
- Network-latency assumptions that gameplay fairness depends on (e.g., an input-to-effect latency
  budget) MUST be stated as an explicit, measurable target in the owning spec's Success Criteria,
  and MUST be revisited if actual measured latency exceeds that target.

## Development Workflow & Quality Gates

- Every pull request that changes gameplay-affecting behavior MUST reference the spec
  requirement(s) or acceptance scenario(s) it implements.
- Every pull request that changes a balance-affecting constant MUST include a one-line rationale
  and MUST update the corresponding spec value in the same change, not in a follow-up.
- Simultaneous/edge-case rule changes (per Principle II) MUST include or update a test that
  exercises the specific simultaneity being resolved.
- Reviewers MUST check new or changed gameplay code against Principle III (server-authoritative
  state) before approval; client-trusting logic for match-outcome-affecting state is a blocking
  review comment, not a style nit.

## Governance

This constitution supersedes ad-hoc practice for all gameplay, fairness, and process decisions in
this project. Where a spec, plan, or task conflicts with this constitution, this constitution
governs unless the constitution itself is first amended.

**Amendment procedure**: Amendments are proposed by editing this file (via `/speckit-constitution`
or direct edit), must state the version bump and rationale in the Sync Impact Report, and take
effect once merged. Any active spec/plan/tasks that conflict with an amendment MUST be flagged for
re-review in the amendment's follow-up notes.

**Versioning policy**: Semantic versioning applies to this document. MAJOR = a principle is removed
or redefined in a backward-incompatible way; MINOR = a new principle or materially expanded section
is added; PATCH = wording, clarification, or typo fixes with no rule-level change.

**Compliance review**: Every `/speckit-plan` MUST include a Constitution Check step confirming the
plan does not violate any Core Principle above; unresolved violations MUST be documented and
justified in the plan's Complexity Tracking section or the plan MUST be revised.

**Version**: 1.0.0 | **Ratified**: 2026-08-14 | **Last Amended**: 2026-08-14
