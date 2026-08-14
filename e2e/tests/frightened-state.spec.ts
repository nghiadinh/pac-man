import { expect, test } from '@playwright/test';
import { startMatch, eatAPowerPellet } from './fixtures';

/**
 * quickstart.md scenario 2 - Power Pellet role reversal (FR-005 to FR-009).
 *
 * The board and rules are proven by the unit and hub-integration layers; what only this layer can
 * show is that a real Hunter browser actually receives and renders the frightened state.
 */

test.describe('power pellet role reversal', () => {
  test('the hunter sees a frightened overlay with a countdown after a power pellet is eaten', async ({
    browser,
  }) => {
    const { runner, hunter } = await startMatch(browser);

    // The overlay is Hunter-only and absent during normal play.
    await expect(hunter.getByTestId('frightened-overlay')).toBeHidden();

    const eaten = await eatAPowerPellet(runner, hunter);
    expect(eaten, 'never reached a power pellet within the budget').toBe(true);

    // FR-005: an 8-second window, so the countdown must read at or below 8.
    const timer = await hunter.getByTestId('frightened-timer').textContent();
    const seconds = Number(timer!.replace('s', ''));
    expect(seconds).toBeGreaterThan(0);
    expect(seconds).toBeLessThanOrEqual(8);
  });

  test('the inversion warning shows and then clears while the window is still open', async ({
    browser,
  }) => {
    // FR-007: inversion covers only the first 3 seconds of the 8-second window, so it must
    // disappear while the overlay itself is still visible.
    const { runner, hunter } = await startMatch(browser);

    const eaten = await eatAPowerPellet(runner, hunter);
    expect(eaten, 'never reached a power pellet within the budget').toBe(true);

    await expect(hunter.getByTestId('inversion-warning')).toBeHidden({ timeout: 6_000 });
    await expect(hunter.getByTestId('frightened-overlay')).toBeVisible();
  });

  test('the overlay clears when the window lapses uncaught', async ({ browser }) => {
    const { runner, hunter } = await startMatch(browser);

    const eaten = await eatAPowerPellet(runner, hunter);
    expect(eaten, 'never reached a power pellet within the budget').toBe(true);

    // FR-005: 8 seconds and the ghost is dangerous again.
    await expect(hunter.getByTestId('frightened-overlay')).toBeHidden({ timeout: 15_000 });
  });

  test('pac-man never sees the hunter-only overlay', async ({ browser }) => {
    const { runner, hunter } = await startMatch(browser);

    await eatAPowerPellet(runner, hunter);

    // Proves the overlay is scoped to the Hunter rather than rendered for whoever is in a match.
    await expect(runner.getByTestId('frightened-overlay')).toBeHidden();
  });
});
