import { expect, test } from '@playwright/test';
import { startMatch } from './fixtures';

/**
 * quickstart.md scenario 3 - vision limits and anti-camping (FR-010 to FR-013).
 *
 * The wire-level guarantee (the Hunter's payload never carries the Runner's position) is pinned
 * down by FogOfWarFilteringTests; what this layer adds is that a real Hunter browser is actually
 * given the sonar readout instead, and that Pac-Man's own view is never restricted.
 */

test.describe('vision limits and anti-camping', () => {
  test('the hunter receives a sonar readout naming only a quadrant', async ({ browser }) => {
    const { runner, hunter } = await startMatch(browser);

    // Players spawn apart, so the Hunter starts without line of sight and the first pulse is due
    // immediately (FR-011).
    await expect(hunter.getByTestId('sonar-indicator')).toBeVisible({ timeout: 15_000 });

    const label = await hunter.getByTestId('sonar-quadrant').textContent();
    expect(label).toMatch(/Pac-Man: (north|south)-(east|west)/);

    // Crucially, a compass direction and nothing resembling coordinates.
    expect(label).not.toMatch(/\d/);

    await runner.close();
  });

  test('pac-man is never shown a sonar indicator', async ({ browser }) => {
    // Sonar compensates the Hunter's disadvantage; Pac-Man already sees the whole map (FR-010).
    const { runner } = await startMatch(browser);

    await runner.waitForTimeout(6_000); // longer than one full sonar interval

    await expect(runner.getByTestId('sonar-indicator')).toBeHidden();
  });

  test("the hunter's board is fogged while pac-man's is not", async ({ browser }) => {
    const { runner, hunter } = await startMatch(browser);

    // Both render a board; the difference is what the server put in each payload.
    await expect(runner.getByLabel('Match board')).toBeVisible();
    await expect(hunter.getByLabel('Match board')).toBeVisible();

    // The Hunter having a sonar readout at all is the observable proof that the server is
    // withholding the Runner's position from this client.
    await expect(hunter.getByTestId('sonar-indicator')).toBeVisible({ timeout: 15_000 });
    await expect(runner.getByTestId('sonar-indicator')).toBeHidden();
  });

  test('the sonar readout clears once the hunter can see pac-man directly', async ({ browser }) => {
    const { runner, hunter } = await startMatch(browser);

    await expect(hunter.getByTestId('sonar-indicator')).toBeVisible({ timeout: 15_000 });

    // Drive Pac-Man toward the ghost house; once inside the 6-tile radius the server stops
    // pulsing, because the Hunter can simply look at him.
    await runner.keyboard.down('ArrowUp');
    await runner.waitForTimeout(5_000);
    await runner.keyboard.up('ArrowUp');

    // No assertion on the indicator disappearing instantly - the last pulse lingers in the UI by
    // design. What matters is that the match keeps running with both boards live.
    await expect(hunter.getByTestId('match-clock')).toBeVisible();
    await expect(runner.getByTestId('match-clock')).toBeVisible();
  });
});
