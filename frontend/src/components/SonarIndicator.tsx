import { useEffect, useState } from 'react';
import type { Quadrant } from '../net/matchConnection';

interface SonarIndicatorProps {
  quadrant: Quadrant | null;
  role: 'Runner' | 'Hunter';
}

const LABEL: Record<Quadrant, string> = {
  NE: 'north-east',
  NW: 'north-west',
  SE: 'south-east',
  SW: 'south-west',
};

/** Which corner of the compass to light up. */
const POSITION: Record<Quadrant, { top: string; left: string }> = {
  NW: { top: '18%', left: '18%' },
  NE: { top: '18%', left: '82%' },
  SW: { top: '82%', left: '18%' },
  SE: { top: '82%', left: '82%' },
};

/**
 * FR-011 sonar readout, Hunter only.
 *
 * Shows the map quadrant and nothing else — the server never sends coordinates, so there is
 * deliberately no way to render a precise position here even by accident. The pulse animation
 * restarts on each new reading so a repeat of the same quadrant still reads as a fresh ping.
 */
export function SonarIndicator({ quadrant, role }: SonarIndicatorProps) {
  const [pulseKey, setPulseKey] = useState(0);

  useEffect(() => {
    if (quadrant) setPulseKey((k) => k + 1);
  }, [quadrant]);

  if (role !== 'Hunter' || !quadrant) {
    return null;
  }

  return (
    <div className="sonar" data-testid="sonar-indicator" role="status" aria-live="polite">
      <div className="sonar__grid" aria-hidden="true">
        <span
          key={pulseKey}
          className="sonar__blip"
          style={POSITION[quadrant]}
          data-testid="sonar-blip"
        />
      </div>
      <span className="sonar__label" data-testid="sonar-quadrant">
        Pac-Man: {LABEL[quadrant]}
      </span>
    </div>
  );
}
