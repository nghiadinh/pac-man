import type { ConnectionPhase } from '../hooks/useMatchConnection';
import type { JoinResultDto } from '../net/matchConnection';

interface JoinScreenProps {
  phase: ConnectionPhase;
  join: JoinResultDto | null;
  error: string | null;
  onJoin: () => void;
}

/**
 * Pre-match screen: join, then wait for the opponent. The 180-second timer does not start until
 * both roles are filled (contract connection lifecycle), so waiting here costs no match time.
 */
export function JoinScreen({ phase, join, error, onJoin }: JoinScreenProps) {
  if (phase === 'error') {
    return (
      <div className="screen">
        <h1>Connection failed</h1>
        <p className="error">{error}</p>
        <button className="button" onClick={onJoin}>
          Retry
        </button>
      </div>
    );
  }

  if (phase === 'waiting' && join) {
    const isRunner = join.role === 'Runner';
    return (
      <div className="screen">
        <h1>Waiting for opponent</h1>
        <span className={`role-badge role-badge--${isRunner ? 'runner' : 'hunter'}`}>
          You are {isRunner ? 'Pac-Man' : 'the Ghost'}
        </span>
        <p>
          {isRunner
            ? 'Clear the maze before the clock runs out. You are faster than the Ghost — use it.'
            : 'You are slower than Pac-Man, so a straight chase can never catch him. Cut him off instead.'}
        </p>
        <p>Match {join.matchId} — share this page to bring in the second player.</p>
      </div>
    );
  }

  return (
    <div className="screen">
      <h1>PAC-MAN 1v1</h1>
      <p>
        One player runs, one player hunts. Three minutes on the clock, three lives on the line.
      </p>
      <button className="button" onClick={onJoin} disabled={phase === 'connecting'}>
        {phase === 'connecting' ? 'Connecting…' : 'Join match'}
      </button>
    </div>
  );
}
