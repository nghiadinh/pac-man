import type { Direction, MatchStateDto, PlayerDto } from '../net/matchConnection';
import { TILE_SIZE, centerOf } from './boardRenderer';

const COLORS = {
  pacman: '#ffe600',
  ghost: '#ff2b2b',
  ghostFrightened: '#2b6bff',
  ghostFrightenedFlash: '#ffffff',
  ghostEyes: '#d0d0ff',
  eyeWhite: '#ffffff',
  pupil: '#000000',
} as const;

const FACING_ANGLE: Record<Direction, number> = {
  Right: 0,
  Down: Math.PI / 2,
  Left: Math.PI,
  Up: -Math.PI / 2,
  None: 0,
};

/** A player whose position the server withheld under fog of war (FR-011) is not drawable. */
function isHidden(player: PlayerDto): boolean {
  return Number.isNaN(player.x) || Number.isNaN(player.y);
}

export function drawPlayers(ctx: CanvasRenderingContext2D, state: MatchStateDto): void {
  if (state.pacman && !isHidden(state.pacman)) {
    drawPacman(ctx, state.pacman, state.elapsedMs);
  }
  if (state.ghost && !isHidden(state.ghost)) {
    drawGhost(ctx, state.ghost, state);
  }
}

function drawPacman(ctx: CanvasRenderingContext2D, pacman: PlayerDto, elapsedMs: number): void {
  const cx = centerOf(pacman.x);
  const cy = centerOf(pacman.y);
  const radius = TILE_SIZE * 0.45;

  // Mouth only animates while actually moving, so a stopped Pac-Man reads as stopped.
  const moving = pacman.facing !== 'None';
  const openness = moving ? Math.abs(Math.sin(elapsedMs / 90)) * 0.32 : 0.12;
  const angle = FACING_ANGLE[pacman.facing];

  ctx.fillStyle = COLORS.pacman;
  ctx.beginPath();
  ctx.moveTo(cx, cy);
  ctx.arc(cx, cy, radius, angle + openness, angle - openness + Math.PI * 2);
  ctx.closePath();
  ctx.fill();
}

function drawGhost(ctx: CanvasRenderingContext2D, ghost: PlayerDto, state: MatchStateDto): void {
  const cx = centerOf(ghost.x);
  const cy = centerOf(ghost.y);
  const radius = TILE_SIZE * 0.45;

  if (ghost.ghostSubState === 'Respawning') {
    return; // sitting out the FR-003 respawn delay
  }

  const eyesOnly = ghost.ghostSubState === 'EyesOnly';

  if (!eyesOnly) {
    ctx.fillStyle = bodyColor(ghost, state);
    ctx.beginPath();
    ctx.arc(cx, cy - radius * 0.15, radius, Math.PI, 0);
    ctx.lineTo(cx + radius, cy + radius * 0.75);
    // Scalloped skirt, three humps.
    for (let i = 0; i < 3; i++) {
      const from = cx + radius - (i * radius * 2) / 3;
      const to = from - (radius * 2) / 3;
      ctx.quadraticCurveTo((from + to) / 2, cy + radius * 0.3, to, cy + radius * 0.75);
    }
    ctx.closePath();
    ctx.fill();
  }

  drawEyes(ctx, cx, cy, radius, ghost.facing, eyesOnly);
}

/**
 * FR-008: the frightened ghost is blue and flashes blue/white. The flash accelerates over the
 * final two seconds so the Hunter can feel the window closing without reading a number.
 */
function bodyColor(ghost: PlayerDto, state: MatchStateDto): string {
  if (ghost.ghostSubState !== 'Frightened' || !state.frightened) {
    return COLORS.ghost;
  }

  const remaining = state.frightened.remainingMs;
  const flashPeriod = remaining < 2000 ? 120 : 300;
  const flashing = Math.floor(state.elapsedMs / flashPeriod) % 2 === 0;

  return flashing ? COLORS.ghostFrightened : COLORS.ghostFrightenedFlash;
}

function drawEyes(
  ctx: CanvasRenderingContext2D,
  cx: number,
  cy: number,
  radius: number,
  facing: Direction,
  eyesOnly: boolean,
): void {
  const offsetX = radius * 0.32;
  const eyeY = cy - radius * 0.25;
  const eyeRadius = radius * 0.26;

  const [lookX, lookY] = {
    Up: [0, -1],
    Down: [0, 1],
    Left: [-1, 0],
    Right: [1, 0],
    None: [0, 0],
  }[facing];

  for (const dx of [-offsetX, offsetX]) {
    ctx.fillStyle = eyesOnly ? COLORS.ghostEyes : COLORS.eyeWhite;
    ctx.beginPath();
    ctx.arc(cx + dx, eyeY, eyeRadius, 0, Math.PI * 2);
    ctx.fill();

    ctx.fillStyle = COLORS.pupil;
    ctx.beginPath();
    ctx.arc(
      cx + dx + lookX * eyeRadius * 0.4,
      eyeY + lookY * eyeRadius * 0.4,
      eyeRadius * 0.5,
      0,
      Math.PI * 2,
    );
    ctx.fill();
  }
}
