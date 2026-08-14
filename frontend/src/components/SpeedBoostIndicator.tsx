import { balanceConstants } from '../generated/balanceConstants';
import type { MatchStateDto } from '../net/matchConnection';

interface SpeedBoostIndicatorProps {
  state: MatchStateDto;
  role: 'Runner' | 'Hunter';
}

/**
 * Whether the Ghost is currently under the FR-012 anti-camping debuff.
 *
 * Derived from the authoritative speed the server reports rather than tracked client-side: the
 * debuffed multiplier (0.95 x 0.85 = 0.8075) is distinctly below normal but above the frightened
 * 0.70, so a midpoint threshold separates them cleanly without duplicating the rule on the client.
 */
export function isGhostDebuffed(state: MatchStateDto): boolean {
  const ghost = state.ghost;
  if (!ghost || ghost.ghostSubState !== 'Normal') {
    return false;
  }

  const { ghostBaseSpeed, ghostSpeedFrightened } = balanceConstants.movement;
  const debuffed = ghostBaseSpeed * (1 - balanceConstants.antiCamping.campSpeedPenalty);
  const threshold = (debuffed + ghostBaseSpeed) / 2;

  return ghost.speedMultiplier < threshold && ghost.speedMultiplier > ghostSpeedFrightened;
}

/**
 * FR-013: tells Pac-Man the Ghost is being penalised for camping, which is the cue to go collect
 * the Power Pellet it was guarding. Shown to Pac-Man only — the Ghost already feels the slowdown.
 */
export function SpeedBoostIndicator({ state, role }: SpeedBoostIndicatorProps) {
  if (role !== 'Runner' || !isGhostDebuffed(state)) {
    return null;
  }

  return (
    <div className="speed-boost" data-testid="speed-boost-indicator" role="status" aria-live="polite">
      <span className="speed-boost__icon" aria-hidden="true">
        ⚡
      </span>
      <span className="speed-boost__text">Ghost is camping — slowed</span>
    </div>
  );
}
