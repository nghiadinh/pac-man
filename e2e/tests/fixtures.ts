import { expect, type Browser, type Page } from '@playwright/test';

export interface MatchPages {
  runner: Page;
  hunter: Page;
}

/**
 * Joins two browser contexts to one match and waits until it is live.
 *
 * The wait between the two joins is load-bearing: role assignment is first-come-first-served, so
 * both clients clicking Join simultaneously can race and land in different matches.
 */
export async function startMatch(browser: Browser): Promise<MatchPages> {
  const runner = await (await browser.newContext()).newPage();
  const hunter = await (await browser.newContext()).newPage();

  await runner.goto('/');
  await runner.getByRole('button', { name: 'Join match' }).click();
  await expect(runner.getByRole('heading', { name: 'Waiting for opponent' })).toBeVisible({
    timeout: 20_000,
  });

  await hunter.goto('/');
  await hunter.getByRole('button', { name: 'Join match' }).click();

  await expect(runner.getByTestId('match-clock')).toBeVisible({ timeout: 20_000 });
  await expect(hunter.getByTestId('match-clock')).toBeVisible({ timeout: 20_000 });

  return { runner, hunter };
}

/**
 * Drives Pac-Man until the score moves, proving pellets are being collected.
 * Returns the score reached.
 */
export async function collectSomePellets(runner: Page): Promise<number> {
  const score = runner.getByTestId('pacman-score');

  await runner.keyboard.down('ArrowLeft');
  await expect
    .poll(async () => Number(await score.textContent()), { timeout: 20_000 })
    .toBeGreaterThan(0);
  await runner.keyboard.up('ArrowLeft');

  return Number(await score.textContent());
}

/**
 * The fixed maze (FR-022), mirrored from backend/src/MatchServer/State/FixedMap.cs.
 *
 * Duplicated here on purpose: it lets the test compute a real route instead of guessing at
 * timings. It is safe to duplicate precisely because the map is fixed and single by requirement -
 * if multi-map support is ever specified, this must be replaced by reading the layout the server
 * already sends in every StateUpdate.
 */
const MAZE = [
  '############################',
  '#o...........##...........o#',
  '#.####.#####.##.#####.####.#',
  '#.####.#####.##.#####.####.#',
  '#.####.#####.##.#####.####.#',
  '#..........................#',
  '#.####.##.########.##.####.#',
  '#......##....##....##......#',
  '######.##### ## #####.######',
  '#....#.##          ##.#....#',
  '#.####.## ###GG### ##.####.#',
  '#.........#      #.........#',
  '#.####.## #      # ##.####.#',
  '#....#.## ######## ##.#....#',
  '######.##          ##.######',
  '#............##............#',
  '#.####.#####.##.#####.####.#',
  '#.####.#####.##.#####.####.#',
  '#o..##.......P .......##..o#',
  '###.##.##.########.##.##.###',
  '#......##....##....##......#',
  '#.##########.##.##########.#',
  '#..........................#',
  '############################'
];

const MAZE_W = MAZE[0].length;
const MAZE_H = MAZE.length;

const isWall = (x: number, y: number): boolean =>
  x < 0 || y < 0 || x >= MAZE_W || y >= MAZE_H || MAZE[y][x] === '#';

const POWER_PELLETS: Array<[number, number]> = MAZE.flatMap((row, y) =>
  [...row].flatMap((cell, x) => (cell === 'o' ? [[x, y] as [number, number]] : [])),
);

const STEPS: Array<{ key: string; dx: number; dy: number }> = [
  { key: 'ArrowLeft', dx: -1, dy: 0 },
  { key: 'ArrowRight', dx: 1, dy: 0 },
  { key: 'ArrowUp', dx: 0, dy: -1 },
  { key: 'ArrowDown', dx: 0, dy: 1 },
];

/** Reads the runner tile this client was sent, mirrored onto the canvas by MatchBoard. */
async function runnerTile(page: Page): Promise<[number, number] | null> {
  const raw = await page.getByLabel('Match board').getAttribute('data-runner-tile');
  if (!raw || raw === '?') return null;
  const [x, y] = raw.split(',').map(Number);
  return [x, y];
}

/** First move of the shortest path from `from` to any tile in `targets`. */
function firstStepToward(
  from: [number, number],
  targets: Array<[number, number]>,
): string | null {
  const goal = new Set(targets.map(([x, y]) => `${x},${y}`));
  const seen = new Set([`${from[0]},${from[1]}`]);
  const queue: Array<{ x: number; y: number; first: string | null }> = [
    { x: from[0], y: from[1], first: null },
  ];

  while (queue.length > 0) {
    const node = queue.shift()!;
    if (node.first && goal.has(`${node.x},${node.y}`)) return node.first;

    for (const step of STEPS) {
      const nx = node.x + step.dx;
      const ny = node.y + step.dy;
      const id = `${nx},${ny}`;
      if (seen.has(id) || isWall(nx, ny)) continue;

      seen.add(id);
      queue.push({ x: nx, y: ny, first: node.first ?? step.key });
    }
  }

  return null;
}

/**
 * Drives Pac-Man to the nearest uneaten power pellet.
 *
 * Re-solves the route from his ACTUAL tile every step rather than replaying a fixed script. Two
 * things make a script unworkable: releasing a key sends "None", which the server reads as "no
 * change in heading" rather than "stop" (classic Pac-Man), so timed key-holds drift; and the
 * pellets sit in the corners, which a blind or greedy walk reaches only by luck. Re-solving each
 * step is immune to both.
 *
 * @returns true if a power pellet was eaten within the budget.
 */
export async function eatAPowerPellet(runner: Page, hunter: Page): Promise<boolean> {
  const overlay = hunter.getByTestId('frightened-overlay');
  // Generous because the whole suite shares one backend: under load each DOM read is
  // slower, so fewer navigation steps fit in a given wall-clock budget. The test timeout
  // is 180s and the assertions after this take ~15s, so 90s still leaves headroom.
  const deadline = Date.now() + 90_000;

  // Hold the current heading the way a player does, swapping keys only when the route turns.
  // Holding matters: Pac-Man travels continuously, so re-solving and re-pressing every cycle
  // fights his own momentum, while a held key simply carries him down the corridor.
  let held: string | null = null;

  const hold = async (key: string) => {
    if (held === key) return;
    if (held) await runner.keyboard.up(held);
    await runner.keyboard.down(key);
    held = key;
  };

  try {
    while (Date.now() < deadline) {
      const here = await runnerTile(runner);
      if (!here) return false;

      const key = firstStepToward(here, POWER_PELLETS);
      if (!key) return false;

      await hold(key);
      await runner.waitForTimeout(100);

      if (await overlay.isVisible()) return true;
    }

    return false;
  } finally {
    if (held) await runner.keyboard.up(held);
  }
}
