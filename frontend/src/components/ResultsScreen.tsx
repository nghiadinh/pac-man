import type { OutcomeDto } from '../net/matchConnection';

interface ResultsScreenProps {
  outcome: OutcomeDto;
  role: 'Runner' | 'Hunter';
}

/** Plain-language explanation of each MatchEndReason, so the result never needs interpreting. */
const REASON_TEXT: Record<OutcomeDto['reason'], string> = {
  PelletsCleared: 'Pac-Man cleared every pellet before the clock ran out.',
  LivesDepleted: 'The Ghost caught Pac-Man three times.',
  TimeoutClearThresholdMet:
    'Time expired with Pac-Man past the 70% mark and ahead on score.',
  TimeoutClearThresholdMissed:
    'Time expired without Pac-Man clearing enough of the maze.',
  Forfeit: 'The opponent disconnected.',
};

export function ResultsScreen({ outcome, role }: ResultsScreenProps) {
  const won = outcome.winner === role;

  return (
    <div className="screen">
      <h1 data-testid="result-headline">{won ? 'You win' : 'You lose'}</h1>

      <span
        className={`role-badge role-badge--${outcome.winner === 'Runner' ? 'runner' : 'hunter'}`}
        data-testid="result-winner"
      >
        {outcome.winner === 'Runner' ? 'Pac-Man wins' : 'Ghost wins'}
      </span>

      <p data-testid="result-reason">{REASON_TEXT[outcome.reason]}</p>

      <div className="results__scores">
        <div>
          <span className="hud__label">Pac-Man</span>
          <span className="hud__value" data-testid="final-pacman-score">
            {outcome.finalPacmanScore}
          </span>
        </div>
        <div>
          <span className="hud__label">Ghost</span>
          <span className="hud__value" data-testid="final-ghost-score">
            {outcome.finalGhostScore}
          </span>
        </div>
      </div>

      <button className="button" onClick={() => window.location.reload()}>
        Play again
      </button>
    </div>
  );
}
