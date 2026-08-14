import { Hud } from './components/Hud';
import { JoinScreen } from './components/JoinScreen';
import { MatchBoard } from './components/MatchBoard';
import { ResultsScreen } from './components/ResultsScreen';
import { useKeyboardInput } from './hooks/useKeyboardInput';
import { useMatchConnection } from './hooks/useMatchConnection';

/**
 * Application shell. React owns the screens and HUD; the live board is painted by the imperative
 * canvas loop inside MatchBoard (research.md §3).
 */
export default function App() {
  const { phase, join, state, stateRef, outcome, error, connect, sendInput } =
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
        <Hud state={state} role={role} />
        <MatchBoard stateRef={stateRef} />
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
