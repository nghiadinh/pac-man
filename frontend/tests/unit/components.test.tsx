import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { FrightenedOverlay } from '../../src/components/FrightenedOverlay';
import { Hud, clearedPercent, formatClock } from '../../src/components/Hud';
import { JoinScreen } from '../../src/components/JoinScreen';
import { ResultsScreen } from '../../src/components/ResultsScreen';
import { SonarIndicator } from '../../src/components/SonarIndicator';
import {
  SpeedBoostIndicator,
  isGhostDebuffed,
} from '../../src/components/SpeedBoostIndicator';
import { balanceConstants } from '../../src/generated/balanceConstants';
import type { MatchStateDto, PlayerDto } from '../../src/net/matchConnection';

function player(overrides: Partial<PlayerDto> = {}): PlayerDto {
  return {
    role: 'Runner',
    x: 1,
    y: 1,
    facing: 'None',
    speedMultiplier: 1,
    livesRemaining: 3,
    ghostSubState: 'Normal',
    connected: true,
    score: 0,
    ...overrides,
  };
}

function state(overrides: Partial<MatchStateDto> = {}): MatchStateDto {
  return {
    matchId: 'test',
    status: 'Active',
    elapsedMs: 0,
    remainingMs: 180_000,
    pacman: player({ role: 'Runner' }),
    ghost: player({ role: 'Hunter', speedMultiplier: balanceConstants.movement.ghostBaseSpeed }),
    map: {
      mapId: 'test',
      width: 3,
      height: 3,
      rows: ['###', '# #', '###'],
      pellets: [],
      powerPellets: [],
      totalPelletCount: 100,
      collectedPelletCount: 0,
    },
    frightened: null,
    scoreChain: 0,
    outcome: null,
    ...overrides,
  };
}

describe('formatClock', () => {
  it('renders mm:ss', () => {
    expect(formatClock(180_000)).toBe('3:00');
    expect(formatClock(65_000)).toBe('1:05');
    expect(formatClock(9_000)).toBe('0:09');
  });

  it('never renders a negative clock', () => {
    expect(formatClock(-5_000)).toBe('0:00');
  });
});

describe('clearedPercent', () => {
  it('is the collected fraction of the board', () => {
    expect(clearedPercent(state({ map: { ...state().map, collectedPelletCount: 70 } }))).toBe(70);
  });

  it('handles an empty board without dividing by zero', () => {
    const empty = state({ map: { ...state().map, totalPelletCount: 0 } });
    expect(clearedPercent(empty)).toBe(0);
  });
});

describe('Hud', () => {
  it('shows both scores, the clock, and remaining lives', () => {
    render(
      <Hud
        state={state({
          pacman: player({ score: 250, livesRemaining: 2 }),
          ghost: player({ role: 'Hunter', score: 500 }),
          remainingMs: 65_000,
        })}
        role="Runner"
      />,
    );

    expect(screen.getByTestId('pacman-score')).toHaveTextContent('250');
    expect(screen.getByTestId('ghost-score')).toHaveTextContent('500');
    expect(screen.getByTestId('match-clock')).toHaveTextContent('1:05');
    expect(screen.getByTestId('pacman-lives')).toHaveTextContent('●●');
  });

  it('flags when Pac-Man is past the 70% threshold that decides a timeout', () => {
    const { rerender } = render(
      <Hud state={state({ map: { ...state().map, collectedPelletCount: 69 } })} role="Runner" />,
    );
    expect(screen.getByTestId('cleared-percent').className).not.toContain('met');

    rerender(
      <Hud state={state({ map: { ...state().map, collectedPelletCount: 70 } })} role="Runner" />,
    );
    expect(screen.getByTestId('cleared-percent').className).toContain('met');
  });
});

describe('FrightenedOverlay', () => {
  const frightened = state({ frightened: { remainingMs: 5_000, inversionActive: true } });

  it('is shown to the Hunter with a countdown', () => {
    render(<FrightenedOverlay state={frightened} role="Hunter" />);

    expect(screen.getByTestId('frightened-overlay')).toBeInTheDocument();
    expect(screen.getByTestId('frightened-timer')).toHaveTextContent('5.0s');
  });

  it('warns about inverted controls only while the inversion window is open', () => {
    const { rerender } = render(<FrightenedOverlay state={frightened} role="Hunter" />);
    expect(screen.getByTestId('inversion-warning')).toBeInTheDocument();

    rerender(
      <FrightenedOverlay
        state={state({ frightened: { remainingMs: 4_000, inversionActive: false } })}
        role="Hunter"
      />,
    );
    expect(screen.queryByTestId('inversion-warning')).not.toBeInTheDocument();
  });

  it('is never shown to Pac-Man', () => {
    render(<FrightenedOverlay state={frightened} role="Runner" />);
    expect(screen.queryByTestId('frightened-overlay')).not.toBeInTheDocument();
  });

  it('is absent when no window is active', () => {
    render(<FrightenedOverlay state={state()} role="Hunter" />);
    expect(screen.queryByTestId('frightened-overlay')).not.toBeInTheDocument();
  });
});

describe('SonarIndicator', () => {
  it('names the quadrant in words and never a coordinate', () => {
    render(<SonarIndicator quadrant="SW" role="Hunter" />);

    const label = screen.getByTestId('sonar-quadrant').textContent!;
    expect(label).toContain('south-west');
    expect(label).not.toMatch(/\d/);
  });

  it('is Hunter-only', () => {
    render(<SonarIndicator quadrant="NE" role="Runner" />);
    expect(screen.queryByTestId('sonar-indicator')).not.toBeInTheDocument();
  });

  it('renders nothing before the first pulse arrives', () => {
    render(<SonarIndicator quadrant={null} role="Hunter" />);
    expect(screen.queryByTestId('sonar-indicator')).not.toBeInTheDocument();
  });
});

describe('isGhostDebuffed', () => {
  const { ghostBaseSpeed, ghostSpeedFrightened } = balanceConstants.movement;
  const debuffed = ghostBaseSpeed * (1 - balanceConstants.antiCamping.campSpeedPenalty);

  it('detects the anti-camping speed', () => {
    expect(isGhostDebuffed(state({ ghost: player({ role: 'Hunter', speedMultiplier: debuffed }) })))
      .toBe(true);
  });

  it('does not mistake normal speed for the debuff', () => {
    expect(
      isGhostDebuffed(state({ ghost: player({ role: 'Hunter', speedMultiplier: ghostBaseSpeed }) })),
    ).toBe(false);
  });

  it('does not mistake the frightened speed for the debuff', () => {
    // The two are separate mechanics; showing "ghost is camping" during a frightened window
    // would tell Pac-Man the wrong thing entirely.
    expect(
      isGhostDebuffed(
        state({
          ghost: player({
            role: 'Hunter',
            speedMultiplier: ghostSpeedFrightened,
            ghostSubState: 'Frightened',
          }),
        }),
      ),
    ).toBe(false);
  });
});

describe('SpeedBoostIndicator', () => {
  const debuffedState = state({
    ghost: player({
      role: 'Hunter',
      speedMultiplier:
        balanceConstants.movement.ghostBaseSpeed *
        (1 - balanceConstants.antiCamping.campSpeedPenalty),
    }),
  });

  it('tells Pac-Man the ghost is camping', () => {
    render(<SpeedBoostIndicator state={debuffedState} role="Runner" />);
    expect(screen.getByTestId('speed-boost-indicator')).toBeInTheDocument();
  });

  it('is not shown to the Hunter, who already feels the slowdown', () => {
    render(<SpeedBoostIndicator state={debuffedState} role="Hunter" />);
    expect(screen.queryByTestId('speed-boost-indicator')).not.toBeInTheDocument();
  });
});

describe('ResultsScreen', () => {
  it('explains each end reason in plain language', () => {
    render(
      <ResultsScreen
        outcome={{
          winner: 'Runner',
          reason: 'PelletsCleared',
          finalPacmanScore: 900,
          finalGhostScore: 500,
        }}
        role="Runner"
      />,
    );

    expect(screen.getByTestId('result-headline')).toHaveTextContent('You win');
    expect(screen.getByTestId('result-reason')).toHaveTextContent('cleared every pellet');
    expect(screen.getByTestId('final-pacman-score')).toHaveTextContent('900');
  });

  it('tells the loser they lost', () => {
    render(
      <ResultsScreen
        outcome={{
          winner: 'Hunter',
          reason: 'LivesDepleted',
          finalPacmanScore: 100,
          finalGhostScore: 1500,
        }}
        role="Runner"
      />,
    );

    expect(screen.getByTestId('result-headline')).toHaveTextContent('You lose');
    expect(screen.getByTestId('result-winner')).toHaveTextContent('Ghost wins');
  });

  it('explains a forfeit', () => {
    render(
      <ResultsScreen
        outcome={{
          winner: 'Runner',
          reason: 'Forfeit',
          finalPacmanScore: 0,
          finalGhostScore: 0,
        }}
        role="Runner"
      />,
    );

    expect(screen.getByTestId('result-reason')).toHaveTextContent('opponent disconnected');
  });
});

describe('JoinScreen', () => {
  it('offers a match without requiring a room code', async () => {
    const onJoin = vi.fn();
    render(<JoinScreen phase="idle" join={null} error={null} onJoin={onJoin} />);

    await userEvent.click(screen.getByTestId('join-button'));

    // Blank means auto-match, so nothing should be passed along.
    expect(onJoin).toHaveBeenCalledWith(undefined);
  });

  it('passes a typed room code through', async () => {
    const onJoin = vi.fn();
    render(<JoinScreen phase="idle" join={null} error={null} onJoin={onJoin} />);

    await userEvent.type(screen.getByTestId('room-code-input'), 'PLAY');
    await userEvent.click(screen.getByTestId('join-button'));

    expect(onJoin).toHaveBeenCalledWith('PLAY');
  });

  it('uppercases as the user types so the field shows what will be sent', async () => {
    render(<JoinScreen phase="idle" join={null} error={null} onJoin={vi.fn()} />);

    const input = screen.getByTestId('room-code-input');
    await userEvent.type(input, 'play');

    expect(input).toHaveValue('PLAY');
  });

  it('refuses characters the server would reject', async () => {
    // I/O/0/1 are excluded server-side; blocking them at the keystroke avoids a pointless
    // round-trip and a confusing error for a code the player cannot even type correctly.
    render(<JoinScreen phase="idle" join={null} error={null} onJoin={vi.fn()} />);

    const input = screen.getByTestId('room-code-input');
    await userEvent.type(input, 'A1IO!B');

    expect(input).toHaveValue('AB');
  });

  it('shows the room code to share while waiting', () => {
    render(
      <JoinScreen
        phase="waiting"
        join={{ matchId: 'K7QM', role: 'Runner', status: 'WaitingForPlayers', started: false }}
        error={null}
        onJoin={vi.fn()}
      />,
    );

    expect(screen.getByTestId('room-code')).toHaveTextContent('K7QM');
    expect(screen.getByText(/Share this/)).toBeInTheDocument();
  });

  it('surfaces a join failure and allows a retry', async () => {
    const onJoin = vi.fn();
    render(
      <JoinScreen
        phase="error"
        join={null}
        error="Room PLAY already has two players."
        onJoin={onJoin}
      />,
    );

    expect(screen.getByTestId('join-error')).toHaveTextContent('already has two players');

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onJoin).toHaveBeenCalled();
  });
});
