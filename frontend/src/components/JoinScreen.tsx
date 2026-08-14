import { useState } from 'react';
import type { ConnectionPhase } from '../hooks/useMatchConnection';
import type { JoinResultDto } from '../net/matchConnection';

interface JoinScreenProps {
  phase: ConnectionPhase;
  join: JoinResultDto | null;
  error: string | null;
  onJoin: (roomCode?: string) => void;
}

/** Mirrors the server's alphabet: uppercase minus the characters people misread (I/1, O/0). */
const CODE_PATTERN = /^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{0,4}$/;

/**
 * Pre-match screen: join, then wait for the opponent. The 180-second timer does not start until
 * both roles are filled, so waiting here costs no match time.
 *
 * Two ways in. Leaving the code blank pairs you with whoever else is waiting. Typing a code you
 * agreed with someone plays THAT person — without it, a third player who happens to click first
 * would take your opponent's slot.
 */
export function JoinScreen({ phase, join, error, onJoin }: JoinScreenProps) {
  const [roomCode, setRoomCode] = useState('');
  const [copied, setCopied] = useState(false);

  const busy = phase === 'connecting';

  const handleCodeChange = (value: string) => {
    // Normalise as the user types so the field always shows what will actually be sent.
    const next = value.toUpperCase().replace(/\s/g, '');
    if (CODE_PATTERN.test(next)) {
      setRoomCode(next);
    }
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (!busy) {
      onJoin(roomCode || undefined);
    }
  };

  if (phase === 'error') {
    return (
      <div className="screen">
        <h1>Could not join</h1>
        <p className="error" data-testid="join-error">
          {error}
        </p>
        <button className="button" onClick={() => onJoin(roomCode || undefined)}>
          Try again
        </button>
      </div>
    );
  }

  if (phase === 'waiting' && join) {
    const isRunner = join.role === 'Runner';

    const copyCode = async () => {
      try {
        await navigator.clipboard.writeText(join.matchId);
        setCopied(true);
        window.setTimeout(() => setCopied(false), 2000);
      } catch {
        // Clipboard access can be denied; the code is on screen to read either way.
      }
    };

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

        <div className="room-code">
          <span className="room-code__label">Room code</span>
          <button
            className="room-code__value"
            onClick={() => void copyCode()}
            title="Copy to clipboard"
            data-testid="room-code"
          >
            {join.matchId}
          </button>
          <span className="room-code__hint">
            {copied ? 'Copied' : 'Share this so they join you, not someone else'}
          </span>
        </div>
      </div>
    );
  }

  return (
    <div className="screen">
      <h1>PAC-MAN 1v1</h1>
      <p>One player runs, one player hunts. Three minutes on the clock, three lives on the line.</p>

      <form className="join" onSubmit={submit}>
        <label className="join__label" htmlFor="room-code-input">
          Room code <span className="join__optional">optional</span>
        </label>

        <input
          id="room-code-input"
          className="join__input"
          value={roomCode}
          onChange={(e) => handleCodeChange(e.target.value)}
          placeholder="e.g. K7QM"
          autoComplete="off"
          spellCheck={false}
          maxLength={4}
          data-testid="room-code-input"
        />

        <button className="button" type="submit" disabled={busy} data-testid="join-button">
          {busy ? 'Connecting…' : roomCode ? `Join room ${roomCode}` : 'Find a match'}
        </button>

        <p className="join__help">
          Leave it blank to play whoever is waiting, or agree a code with a friend and both enter
          it — whichever of you arrives first opens the room.
        </p>
      </form>
    </div>
  );
}
