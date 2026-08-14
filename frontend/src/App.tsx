import { JoinScreen } from './components/JoinScreen';
import { useMatchConnection } from './hooks/useMatchConnection';

/**
 * Application shell. React owns the screens and HUD; the live match board is painted by an
 * imperative canvas loop mounted inside MatchBoard (research.md §3), which arrives with US1.
 */
export default function App() {
  const { phase, join, error, connect } = useMatchConnection();

  const inMatch = phase === 'playing' || phase === 'ended';

  return (
    <div className="app">
      {inMatch ? (
        <div className="screen">
          <h1>Match in progress</h1>
          <p>
            The board renderer and HUD land with User Story 1. The connection, role assignment,
            and authoritative state feed are live now.
          </p>
        </div>
      ) : (
        <JoinScreen phase={phase} join={join} error={error} onJoin={() => void connect()} />
      )}
    </div>
  );
}
