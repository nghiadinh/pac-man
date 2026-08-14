import { useEffect, useRef } from 'react';
import type { MatchStateDto } from '../net/matchConnection';
import { boardPixelSize, drawBoard, drawFogOfWar } from '../render/boardRenderer';
import { drawPlayers } from '../render/spriteRenderer';

interface MatchBoardProps {
  /** Latest server snapshot, held in a ref so the draw loop never depends on a re-render. */
  stateRef: React.MutableRefObject<MatchStateDto | null>;
  /** The local player's role - the Hunter's view is fogged (FR-011). */
  role: 'Runner' | 'Hunter';
}

/**
 * React mounts and sizes the canvas; an imperative requestAnimationFrame loop owns everything
 * inside it (research.md §3). The loop reads from a ref rather than props, so it runs at display
 * refresh rate independently of the ~30Hz server tick that drives React state.
 */
/** Tile coordinate as "x,y", or "?" when the server withheld the position (FR-011). */
function tileAttr(x: number | null | undefined, y: number | null | undefined): string {
  if (x === null || x === undefined || y === null || y === undefined) {
    return '?';
  }
  return `${Math.round(x)},${Math.round(y)}`;
}

export function MatchBoard({ stateRef, role }: MatchBoardProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    let frame = 0;
    let sized = false;

    const render = () => {
      frame = requestAnimationFrame(render);

      const state = stateRef.current;
      if (!state) return;

      if (!sized) {
        const { width, height } = boardPixelSize(state);
        canvas.width = width;
        canvas.height = height;
        sized = true;
      }

      drawBoard(ctx, state);
      drawPlayers(ctx, state);

      // Drawn last so it dims sprites too. Runner is never fogged (FR-010).
      if (role === 'Hunter') {
        drawFogOfWar(ctx, state);
      }

      // Mirror the positions this client was actually sent onto the element, so end-to-end tests
      // can navigate the maze instead of guessing at timings. This exposes nothing extra: a
      // fogged-out opponent is already absent from this client's payload and shows as "?".
      canvas.dataset.runnerTile = tileAttr(state.pacman?.x, state.pacman?.y);
      canvas.dataset.hunterTile = tileAttr(state.ghost?.x, state.ghost?.y);
    };

    frame = requestAnimationFrame(render);
    return () => cancelAnimationFrame(frame);
  }, [stateRef, role]);

  return <canvas ref={canvasRef} aria-label="Match board" />;
}
