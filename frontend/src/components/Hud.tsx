import type { MatchStateDto } from '../net/matchConnection';

interface HudProps {
  state: MatchStateDto;
  role: 'Runner' | 'Hunter';
}

/** mm:ss from the server's authoritative remaining time. */
export function formatClock(remainingMs: number): string {
  const totalSeconds = Math.max(0, Math.ceil(remainingMs / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

export function clearedPercent(state: MatchStateDto): number {
  const { totalPelletCount, collectedPelletCount } = state.map;
  return totalPelletCount === 0 ? 0 : (collectedPelletCount / totalPelletCount) * 100;
}

/**
 * Score, clock, lives, and clear progress (FR-019). Ordinary React - it updates once per server
 * tick, not per animation frame, so there is nothing here that needs the canvas treatment.
 */
export function Hud({ state, role }: HudProps) {
  const cleared = clearedPercent(state);
  // The 70% mark decides a timeout (FR-017), so it is worth showing rather than making players
  // infer it from a raw pellet count.
  const meetsThreshold = cleared >= 70;

  return (
    <div className="hud">
      <div className="hud__group">
        <span className="hud__label">Pac-Man</span>
        <span className="hud__value" data-testid="pacman-score">
          {state.pacman?.score ?? 0}
        </span>
        <span className="hud__lives" data-testid="pacman-lives">
          {'●'.repeat(Math.max(0, state.pacman?.livesRemaining ?? 0))}
        </span>
      </div>

      <div className="hud__group hud__group--center">
        <span className="hud__clock" data-testid="match-clock">
          {formatClock(state.remainingMs)}
        </span>
        <span
          className={`hud__cleared ${meetsThreshold ? 'hud__cleared--met' : ''}`}
          data-testid="cleared-percent"
        >
          {cleared.toFixed(0)}% cleared
        </span>
      </div>

      <div className="hud__group hud__group--right">
        <span className="hud__label">Ghost</span>
        <span className="hud__value" data-testid="ghost-score">
          {state.ghost?.score ?? 0}
        </span>
        <span className="hud__role">You are {role === 'Runner' ? 'Pac-Man' : 'the Ghost'}</span>
      </div>
    </div>
  );
}
