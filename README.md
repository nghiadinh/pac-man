# Pac-Man 1v1

An asymmetric two-player Pac-Man: one human runs, one human hunts. Three minutes on the clock,
three lives on the line.

The interesting problem here is balance. A human ghost is far harder to escape than the classic
AI — it cuts off escape routes, camps power pellets, and waits at intersections — so the rules are
deliberately lopsided to compensate: Pac-Man is permanently faster, the Ghost's vision is limited,
and camping is actively punished.

Full specification: [`specs/001-multiplayer-gameplay-balance/spec.md`](specs/001-multiplayer-gameplay-balance/spec.md)

## Quick start

Requires **.NET 10 SDK** and **Node.js 20+**.

```powershell
./run.ps1
```

That starts both servers, waits until they answer, and opens two browser windows — the game needs
two clients, since a match cannot start until both roles are filled. Click **Join match** in the
first window, wait for "Waiting for opponent", then click it in the second.

Controls: arrow keys or WASD. Ctrl+C stops everything.

```powershell
./run.ps1 -NoBrowser     # servers only
./run.ps1 -Windowed      # each server in its own console
./run.ps1 -Stop          # free the ports after a force-kill left servers behind
```

Or by hand, in two terminals:

```bash
dotnet run --project backend/src/MatchServer --urls http://localhost:5080
npm install --prefix frontend; npm run dev --prefix frontend
```

## Architecture

```
backend/    ASP.NET Core + SignalR. Owns ALL gameplay state.
frontend/   React shell + HUD; a plain canvas loop paints the live board.
shared/     balance-constants.json + codegen for both languages.
e2e/        Playwright, two browser contexts per test.
```

Two decisions shape everything else:

**The server is the only authority.** Clients send a direction and render what they are told.
Nothing about position, speed, collision, or score is ever trusted from a client. This is not
defensive habit — the whole balance model rests on margins as fine as 5%, which a client that
could misreport itself would erase completely.

**React does not draw the board.** React owns the screens, HUD, and indicators; a plain
`requestAnimationFrame` loop inside one React-owned `<canvas>` owns the maze and sprites. Diffing a
React tree for two moving actors at 60fps would spend the latency budget on bookkeeping. See
[`research.md` §3](specs/001-multiplayer-gameplay-balance/research.md).

## Balance constants

Every tuned number — speeds, durations, radii, point values — lives in
[`shared/balance-constants.json`](shared/balance-constants.json) and is code-generated into
`BalanceConstants.cs` and `balanceConstants.ts` at build time.

```bash
node shared/codegen/generate.js
```

**Never edit the generated files.** They are gitignored build artifacts. Changing a balance value
means editing the JSON and updating the owning requirement in `spec.md` in the same change — the
project constitution treats a balance change without a spec change as out of process.

One caveat: `baseTilesPerSecond` is *not* spec-derived. The spec fixes only the ratios between
roles (100% vs 95%, and so on), never an absolute pace, so that value changes feel rather than
balance.

## Tests

Three layers, each catching what the others structurally cannot:

```bash
dotnet test                     # 118 unit + 27 integration
npm test --prefix frontend      # 27 component/render
```

```powershell
./run-e2e.ps1                          # 17 end-to-end, in visible browsers
./run-e2e.ps1 -Spec core-match-loop    # just the core loop, ~3 min
./run-e2e.ps1 -Headless                # no windows, for CI
./run-e2e.ps1 -Report                  # browse the last run's traces and video
```

Playwright starts and stops the servers itself, so `run-e2e.ps1` does not need `run.ps1` first.

| Layer | Answers |
|---|---|
| xUnit unit | Is each rule correct in isolation? |
| SignalR `TestServer` | Does the hub actually call those rules, over a real connection? |
| Playwright | Do two real browsers observe a fair, playable match? |

The end-to-end layer is not redundant. Two guarantees only exist on the wire: that the Hunter's
payload genuinely omits Pac-Man's position under fog of war, and that the latency budget holds.
Neither can be proven by a rule tested in isolation.

### Notes for anyone editing tests

- Integration tests run **sequentially** (`AssemblyInfo.cs`). Each boots its own server with a
  30Hz loop; in parallel they starve each other and game-time deadlines flake.
- In end-to-end tests, **hold** movement keys rather than tapping or using timed presses.
  Releasing a key sends `None`, which the server reads as "no change in heading" rather than
  "stop" — so Pac-Man keeps travelling and any timed script drifts out of sync with his real
  position. `e2e/tests/fixtures.ts` navigates by re-solving a route from the position the client
  actually received.

## Spec-driven workflow

This project is built with [Spec Kit](https://github.com/github/spec-kit). The specification,
plan, data model, contract, and task list are the source of truth and live under
`specs/001-multiplayer-gameplay-balance/`. Project principles — including the server-authority and
one-edit-site rules above — are in [`.specify/memory/constitution.md`](.specify/memory/constitution.md).
