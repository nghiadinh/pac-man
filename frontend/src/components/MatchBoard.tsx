import { useEffect, useRef } from 'react';
import type { MatchStateDto } from '../net/matchConnection';
import { boardPixelSize, drawBoard } from '../render/boardRenderer';
import { drawPlayers } from '../render/spriteRenderer';

interface MatchBoardProps {
  /** Latest server snapshot, held in a ref so the draw loop never depends on a re-render. */
  stateRef: React.MutableRefObject<MatchStateDto | null>;
}

/**
 * React mounts and sizes the canvas; an imperative requestAnimationFrame loop owns everything
 * inside it (research.md §3). The loop reads from a ref rather than props, so it runs at display
 * refresh rate independently of the ~30Hz server tick that drives React state.
 */
export function MatchBoard({ stateRef }: MatchBoardProps) {
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
    };

    frame = requestAnimationFrame(render);
    return () => cancelAnimationFrame(frame);
  }, [stateRef]);

  return <canvas ref={canvasRef} aria-label="Match board" />;
}
