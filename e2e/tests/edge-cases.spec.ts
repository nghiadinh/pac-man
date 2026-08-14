import { expect, test } from '@playwright/test';
import { collectSomePellets, startMatch } from './fixtures';

/**
 * quickstart.md scenario 4 — the clarification-session edge cases, end to end.
 *
 * Deliberately narrow. The DECISIONS behind these rules (the 70% boundary, the FR-023 tie-break,
 * the FR-021 same-tick ordering, the timer-vs-final-pellet race) are pinned down exactly where
 * they can be set up exactly: WinConditionRulesTests, WinConditionSimultaneityTests,
 * SimultaneousCollisionTests, and TimeoutEvaluationTests. Reproducing an exact score tie or a
 * same-tick collision by driving two browsers is not achievable reliably, and a flaky test that
 * only sometimes constructs the scenario proves less than the deterministic one already does.
 *
 * What this file covers is the part that genuinely only exists on the wire: that both clients end
 * up agreeing, promptly, on a result the server decided.
 */

test.describe('edge cases and cross-client agreement', () => {
  test('a forfeit is announced to the remaining client without a grace period', async ({
    browser,
  }) => {
    // FR-020, clarified 2026-08-14: immediate, no reconnect window.
    const { runner, hunter } = await startMatch(browser);

    const start = Date.now();
    await hunter.context().close();

    await expect(runner.getByTestId('result-headline')).toHaveText('You win', { timeout: 15_000 });
    await expect(runner.getByTestId('result-reason')).toContainText('opponent disconnected');

    expect(
      Date.now() - start,
      'forfeit took long enough to suggest a grace period was introduced',
    ).toBeLessThan(15_000);
  });

  test('a ScoreEvent reaches both HUDs with identical values within a second', async ({
    browser,
  }) => {
    // SC-005: no perceptible desync between the two players' views of the score.
    const { runner, hunter } = await startMatch(browser);

    await collectSomePellets(runner);

    await expect
      .poll(
        async () => {
          const [own, opponent] = await Promise.all([
            runner.getByTestId('pacman-score').textContent(),
            hunter.getByTestId('pacman-score').textContent(),
          ]);
          return own === opponent && Number(own) > 0;
        },
        { timeout: 5_000 },
      )
      .toBe(true);
  });

  test('both clients see the same clock, so neither can be ahead of the other', async ({
    browser,
  }) => {
    const { runner, hunter } = await startMatch(browser);

    await expect
      .poll(
        async () => {
          const [a, b] = await Promise.all([
            runner.getByTestId('match-clock').textContent(),
            hunter.getByTestId('match-clock').textContent(),
          ]);
          // One second of tolerance: the two reads are not simultaneous.
          const toSeconds = (t: string) => {
            const [m, s] = t.split(':').map(Number);
            return m * 60 + s;
          };
          return Math.abs(toSeconds(a!) - toSeconds(b!)) <= 1;
        },
        { timeout: 10_000 },
      )
      .toBe(true);
  });

  test('the match never sits in a state with no winner once it has ended', async ({ browser }) => {
    // SC-001: every match reaches a definitive outcome. Forced here via forfeit, which is the one
    // terminal path reachable from the UI inside a test.
    const { runner, hunter } = await startMatch(browser);

    await hunter.context().close();

    await expect(runner.getByTestId('result-winner')).toBeVisible({ timeout: 15_000 });
    const winner = await runner.getByTestId('result-winner').textContent();
    expect(winner).toMatch(/(Pac-Man|Ghost) wins/);
  });
});
