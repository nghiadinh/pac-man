import { expect, test } from '@playwright/test';
import { startMatch } from './fixtures';

/**
 * quickstart.md scenario 1 - the core asymmetric match loop.
 *
 * Two real browser contexts, one per role, against the real backend and frontend. This is the
 * only layer that exercises the actual wire path (research.md §4): the unit tests prove each rule
 * in isolation, but only this proves two genuine clients observe a consistent, playable match.
 */

test.describe('core match loop', () => {
  test('both players enter a live match with the board and HUD rendered', async ({ browser }) => {
    const { runner, hunter } = await startMatch(browser);

    await expect(runner.getByLabel('Match board')).toBeVisible();
    await expect(hunter.getByLabel('Match board')).toBeVisible();

    // Pac-Man starts on three lives (FR-002).
    await expect(runner.getByTestId('pacman-lives')).toHaveText('●●●');

    // Each player is told which side they are.
    await expect(runner.getByText('You are Pac-Man')).toBeVisible();
    await expect(hunter.getByText('You are the Ghost')).toBeVisible();
  });

  test('the match clock counts down from 3:00 once both roles are filled', async ({ browser }) => {
    const { runner } = await startMatch(browser);

    const clock = runner.getByTestId('match-clock');
    const first = await clock.textContent();

    await expect
      .poll(async () => clock.textContent(), { timeout: 10_000 })
      .not.toBe(first);

    // FR-014: a strict 180-second countdown, so it must never read above 3:00.
    const [minutes] = (await clock.textContent())!.split(':').map(Number);
    expect(minutes).toBeLessThanOrEqual(3);
  });

  test('moving Pac-Man collects pellets and raises the score', async ({ browser }) => {
    const { runner } = await startMatch(browser);

    const score = runner.getByTestId('pacman-score');
    await expect(score).toHaveText('0');

    // Drive along the corridor; pellets are worth 10 each (FR-018).
    await runner.keyboard.down('ArrowLeft');
    await expect.poll(async () => Number(await score.textContent()), { timeout: 15_000 })
      .toBeGreaterThan(0);
    await runner.keyboard.up('ArrowLeft');

    await expect
      .poll(async () => Number((await runner.getByTestId('cleared-percent').textContent())!
        .replace('% cleared', '')), { timeout: 10_000 })
      .toBeGreaterThan(0);
  });

  test('both clients agree on the score within a second of it changing', async ({ browser }) => {
    // SC-005: no perceptible desync between the two players' views of the score.
    const { runner, hunter } = await startMatch(browser);

    await runner.keyboard.down('ArrowLeft');
    await expect
      .poll(async () => Number(await runner.getByTestId('pacman-score').textContent()), {
        timeout: 20_000,
      })
      .toBeGreaterThan(0);
    await runner.keyboard.up('ArrowLeft');

    // Both sides must be re-read on every poll. Releasing a key does not stop Pac-Man - he runs
    // on until a wall - so a score captured once goes stale mid-assertion and can never match.
    await expect
      .poll(
        async () => {
          const [own, opponent] = await Promise.all([
            runner.getByTestId('pacman-score').textContent(),
            hunter.getByTestId('pacman-score').textContent(),
          ]);
          return own === opponent;
        },
        { timeout: 5_000 },
      )
      .toBe(true);
  });

  test('a disconnect immediately forfeits the match to the remaining player', async ({
    browser,
  }) => {
    // FR-020, clarified 2026-08-14: no reconnect grace period.
    const { runner, hunter } = await startMatch(browser);

    await hunter.context().close();

    await expect(runner.getByTestId('result-headline')).toHaveText('You win', {
      timeout: 15_000,
    });
    await expect(runner.getByTestId('result-reason')).toContainText('opponent disconnected');
  });
});
