import type { MatchStateDto } from '../net/matchConnection';

export const TILE_SIZE = 20;

const COLORS = {
  background: '#000000',
  wall: '#1a1aa8',
  pellet: '#ffb897',
  powerPellet: '#ffffff',
} as const;

/**
 * Paints the maze, pellets, and power pellets.
 *
 * Deliberately imperative and outside React: this runs every animation frame, and re-rendering a
 * React tree per tile at 60fps would add virtual-DOM work the SC-006 latency budget has no room
 * for (research.md §3). React owns the shell and HUD; this owns pixels.
 */
export function drawBoard(ctx: CanvasRenderingContext2D, state: MatchStateDto): void {
  const { map } = state;

  ctx.fillStyle = COLORS.background;
  ctx.fillRect(0, 0, map.width * TILE_SIZE, map.height * TILE_SIZE);

  drawWalls(ctx, map.rows);
  drawPellets(ctx, state);
}

function drawWalls(ctx: CanvasRenderingContext2D, rows: string[]): void {
  ctx.fillStyle = COLORS.wall;

  for (let y = 0; y < rows.length; y++) {
    const row = rows[y];
    for (let x = 0; x < row.length; x++) {
      if (row[x] === '#') {
        ctx.fillRect(x * TILE_SIZE, y * TILE_SIZE, TILE_SIZE, TILE_SIZE);
      }
    }
  }
}

function drawPellets(ctx: CanvasRenderingContext2D, state: MatchStateDto): void {
  ctx.fillStyle = COLORS.pellet;
  for (const pellet of state.map.pellets) {
    if (pellet.collected) continue;
    ctx.beginPath();
    ctx.arc(centerOf(pellet.x), centerOf(pellet.y), TILE_SIZE * 0.1, 0, Math.PI * 2);
    ctx.fill();
  }

  // Power pellets pulse so they read as the strategically important tiles they are - they are
  // what the anti-camping rule (FR-012) exists to protect.
  const pulse = 0.85 + 0.15 * Math.sin(state.elapsedMs / 150);
  ctx.fillStyle = COLORS.powerPellet;
  for (const power of state.map.powerPellets) {
    if (power.collected) continue;
    ctx.beginPath();
    ctx.arc(centerOf(power.x), centerOf(power.y), TILE_SIZE * 0.3 * pulse, 0, Math.PI * 2);
    ctx.fill();
  }
}

export function centerOf(tile: number): number {
  return tile * TILE_SIZE + TILE_SIZE / 2;
}

export function boardPixelSize(state: MatchStateDto): { width: number; height: number } {
  return {
    width: state.map.width * TILE_SIZE,
    height: state.map.height * TILE_SIZE,
  };
}
