import { useCallback, useEffect, useRef, useState } from 'react';
import {
  MatchConnection,
  type Direction,
  type JoinResultDto,
  type MatchStateDto,
  type OutcomeDto,
  type Quadrant,
  type ScoreEventDto,
} from '../net/matchConnection';

export type ConnectionPhase = 'idle' | 'connecting' | 'waiting' | 'playing' | 'ended' | 'error';

export interface MatchConnectionApi {
  phase: ConnectionPhase;
  join: JoinResultDto | null;
  /** Latest snapshot, for React-rendered UI (HUD, screens). Updates once per server tick. */
  state: MatchStateDto | null;
  /**
   * The same snapshot in a ref. The canvas draw loop reads THIS, not `state`, so painting the
   * board at 60fps never depends on a React re-render (research.md §3).
   */
  stateRef: React.MutableRefObject<MatchStateDto | null>;
  outcome: OutcomeDto | null;
  lastScoreEvent: ScoreEventDto | null;
  sonarQuadrant: Quadrant | null;
  error: string | null;
  connect: () => Promise<void>;
  sendInput: (direction: Direction) => void;
}

export function useMatchConnection(): MatchConnectionApi {
  const connectionRef = useRef<MatchConnection | null>(null);
  const stateRef = useRef<MatchStateDto | null>(null);

  const [phase, setPhase] = useState<ConnectionPhase>('idle');
  const [join, setJoin] = useState<JoinResultDto | null>(null);
  const [state, setState] = useState<MatchStateDto | null>(null);
  const [outcome, setOutcome] = useState<OutcomeDto | null>(null);
  const [lastScoreEvent, setLastScoreEvent] = useState<ScoreEventDto | null>(null);
  const [sonarQuadrant, setSonarQuadrant] = useState<Quadrant | null>(null);
  const [error, setError] = useState<string | null>(null);

  const connect = useCallback(async () => {
    if (connectionRef.current) {
      return;
    }

    setPhase('connecting');
    setError(null);

    const connection = new MatchConnection();
    connectionRef.current = connection;

    try {
      const result = await connection.connect({
        onStateUpdate: (next) => {
          stateRef.current = next;
          setState(next);
          setPhase(
            next.status === 'Active' ? 'playing' : next.status === 'Ended' ? 'ended' : 'waiting',
          );
        },
        onScoreEvent: setLastScoreEvent,
        onSonarPulse: setSonarQuadrant,
        onMatchEnded: (result) => {
          setOutcome(result);
          setPhase('ended');
        },
      });

      setJoin(result);
      setPhase(result.started ? 'playing' : 'waiting');
    } catch (cause) {
      connectionRef.current = null;
      setError(cause instanceof Error ? cause.message : String(cause));
      setPhase('error');
    }
  }, []);

  const sendInput = useCallback((direction: Direction) => {
    // Fire and forget: input is a per-tick intent, so a dropped send is corrected by the next
    // keypress rather than being worth surfacing as an error.
    void connectionRef.current?.sendInput(direction);
  }, []);

  useEffect(() => {
    return () => {
      void connectionRef.current?.disconnect();
      connectionRef.current = null;
    };
  }, []);

  return {
    phase,
    join,
    state,
    stateRef,
    outcome,
    lastScoreEvent,
    sonarQuadrant,
    error,
    connect,
    sendInput,
  };
}
