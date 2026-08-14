import { FrightenedOverlay } from './components/FrightenedOverlay';
import { Hud } from './components/Hud';
import { JoinScreen } from './components/JoinScreen';
import { MatchBoard } from './components/MatchBoard';
import { ResultsScreen } from './components/ResultsScreen';
import { SonarIndicator } from './components/SonarIndicator';
import { SpeedBoostIndicator } from './components/SpeedBoostIndicator';
import { useKeyboardInput } from './hooks/useKeyboardInput';
import { useMatchConnection } from './hooks/useMatchConnection';

/**
 * Application shell. React owns the screens, HUD, and indicators; the live board is painted by the
 * imperative canvas loop inside MatchBoard (research.md §3).
 */
export default function App() {
  const { phase, join, state, stateRef, outcome, sonarQuadrant, error, connect, sendInput } =
    useMatchConnection();

  useKeyboardInput(sendInput, phase === 'playing');

  const role = join?.role ?? 'Runner';

  if (phase === 'ended' && outcome) {
    return (
      <div className="app">
        <ResultsScreen outcome={outcome} role={role} />
      </div>
    );
  }

  if (phase === 'playing' && state) {
    return (
      <div className="app">
        <FrightenedOverlay state={state} role={role} />
        <Hud state={state} role={role} />
        <MatchBoard stateRef={stateRef} role={role} />
        <div className="indicators">
          <SonarIndicator quadrant={sonarQuadrant} role={role} />
          <SpeedBoostIndicator state={state} role={role} />
        </div>
        <p className="hint">Arrow keys or WASD to move</p>
      </div>
    );
  }

  return (
    <div className="app">
      <JoinScreen phase={phase} join={join} error={error} onJoin={() => void connect()} />
    </div>
  );
}
