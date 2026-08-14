import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';

const HUB_URL = import.meta.env.VITE_HUB_URL ?? 'http://localhost:5080/hubs/match';

/** Mirrors MatchStateDto on the server (contracts/match-room-protocol.md). */
export interface MatchStateDto {
  matchId: string;
  status: 'WaitingForPlayers' | 'Active' | 'Ended';
  elapsedMs: number;
  remainingMs: number;
  pacman: PlayerDto | null;
  ghost: PlayerDto | null;
  map: MapDto;
  frightened: FrightenedDto | null;
  scoreChain: number;
  outcome: OutcomeDto | null;
}

export interface PlayerDto {
  role: 'Runner' | 'Hunter';
  /** NaN when the server withheld position under fog of war (FR-011). */
  x: number;
  y: number;
  facing: Direction;
  speedMultiplier: number;
  livesRemaining: number;
  ghostSubState: 'Normal' | 'Frightened' | 'EyesOnly' | 'Respawning';
  connected: boolean;
  score: number;
}

export interface MapDto {
  mapId: string;
  width: number;
  height: number;
  /** One string per row; '#' is wall, ' ' is walkable. */
  rows: string[];
  pellets: PelletDto[];
  powerPellets: PelletDto[];
  totalPelletCount: number;
  collectedPelletCount: number;
}

export interface PelletDto {
  x: number;
  y: number;
  collected: boolean;
}

export interface FrightenedDto {
  remainingMs: number;
  inversionActive: boolean;
}

export interface OutcomeDto {
  winner: 'Runner' | 'Hunter';
  reason:
    | 'PelletsCleared'
    | 'LivesDepleted'
    | 'TimeoutClearThresholdMet'
    | 'TimeoutClearThresholdMissed'
    | 'Forfeit';
  finalPacmanScore: number;
  finalGhostScore: number;
}

export interface JoinResultDto {
  matchId: string;
  role: 'Runner' | 'Hunter';
  status: string;
  started: boolean;
}

export interface ScoreEventDto {
  eventType:
    | 'PelletCollected'
    | 'PowerPelletCollected'
    | 'GhostCaught'
    | 'PacmanEliminated'
    | 'TimeBonus';
  points: number;
  recipient: 'Runner' | 'Hunter';
}

export type Direction = 'None' | 'Up' | 'Down' | 'Left' | 'Right';

export type Quadrant = 'NE' | 'NW' | 'SE' | 'SW';

export interface MatchHandlers {
  onStateUpdate?: (state: MatchStateDto) => void;
  onScoreEvent?: (event: ScoreEventDto) => void;
  onSonarPulse?: (quadrant: Quadrant) => void;
  onMatchEnded?: (outcome: OutcomeDto) => void;
}

/**
 * Thin transport wrapper around the SignalR hub.
 *
 * Deliberately contains NO game logic: it forwards input intent up and hands server state down.
 * Every gameplay decision belongs to the backend (Constitution Principle III), so anything that
 * looks like a rule appearing in this file is a bug.
 */
export class MatchConnection {
  private connection: HubConnection | null = null;
  private lastSentDirection: Direction = 'None';

  async connect(handlers: MatchHandlers): Promise<JoinResultDto> {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([]) // FR-020 makes a disconnect a forfeit; retrying would be a lie
      .configureLogging(LogLevel.Warning)
      .build();

    if (handlers.onStateUpdate) {
      connection.on('StateUpdate', handlers.onStateUpdate);
    }
    if (handlers.onScoreEvent) {
      connection.on('ScoreEvent', handlers.onScoreEvent);
    }
    if (handlers.onSonarPulse) {
      connection.on('SonarPulse', handlers.onSonarPulse);
    }
    if (handlers.onMatchEnded) {
      connection.on('MatchEnded', handlers.onMatchEnded);
    }

    await connection.start();
    this.connection = connection;

    return connection.invoke<JoinResultDto>('JoinMatch');
  }

  /**
   * Sends a direction change. Repeats are suppressed: the contract says to send on CHANGE, not
   * once per frame, so a held key does not flood the hub at 60Hz.
   */
  async sendInput(direction: Direction): Promise<void> {
    if (direction === this.lastSentDirection) {
      return;
    }
    if (this.connection?.state !== HubConnectionState.Connected) {
      return;
    }

    this.lastSentDirection = direction;
    await this.connection.invoke('SendInput', direction);
  }

  async disconnect(): Promise<void> {
    await this.connection?.stop();
    this.connection = null;
    this.lastSentDirection = 'None';
  }

  get isConnected(): boolean {
    return this.connection?.state === HubConnectionState.Connected;
  }
}
