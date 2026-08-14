import type { MatchStateDto } from '../net/matchConnection';

interface FrightenedOverlayProps {
  state: MatchStateDto;
  role: 'Runner' | 'Hunter';
}

/**
 * FR-008: the Ghost's frightened HUD — a blue vignette pulse and a countdown.
 *
 * Shown only to the Hunter. Pac-Man already gets the information from the ghost turning blue on
 * the board; the vignette exists to make the Hunter *feel* the debuff, and putting it on both
 * screens would just obscure Pac-Man's view for no reason.
 */
export function FrightenedOverlay({ state, role }: FrightenedOverlayProps) {
  if (role !== 'Hunter' || !state.frightened) {
    return null;
  }

  const { remainingMs, inversionActive } = state.frightened;
  const seconds = (remainingMs / 1000).toFixed(1);

  return (
    <div
      className={`frightened ${remainingMs < 2000 ? 'frightened--ending' : ''}`}
      data-testid="frightened-overlay"
      role="status"
      aria-live="polite"
    >
      <div className="frightened__vignette" aria-hidden="true" />
      <div className="frightened__panel">
        <span className="frightened__label">Vulnerable</span>
        <span className="frightened__timer" data-testid="frightened-timer">
          {seconds}s
        </span>
        {inversionActive && (
          <span className="frightened__inverted" data-testid="inversion-warning">
            Controls inverted
          </span>
        )}
      </div>
    </div>
  );
}
