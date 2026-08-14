import { useEffect } from 'react';
import type { Direction } from '../net/matchConnection';

const KEY_MAP: Record<string, Direction> = {
  ArrowUp: 'Up',
  ArrowDown: 'Down',
  ArrowLeft: 'Left',
  ArrowRight: 'Right',
  KeyW: 'Up',
  KeyS: 'Down',
  KeyA: 'Left',
  KeyD: 'Right',
};

export function directionForKey(code: string): Direction | null {
  return KEY_MAP[code] ?? null;
}

/**
 * Forwards keyboard intent to the server.
 *
 * Sends on CHANGE, not per frame - a held key produces one message, not 60 a second. Note this
 * sends the player's TRUE direction even when the Ghost is inverted (FR-007): the inversion is
 * applied server-side, because a client that applied it locally could simply choose not to
 * (Constitution Principle III).
 */
export function useKeyboardInput(
  sendInput: (direction: Direction) => void,
  enabled: boolean,
): void {
  useEffect(() => {
    if (!enabled) return;

    const held: string[] = [];

    const onKeyDown = (event: KeyboardEvent) => {
      const direction = directionForKey(event.code);
      if (!direction) return;

      event.preventDefault();

      if (!held.includes(event.code)) {
        held.push(event.code);
      }
      sendInput(direction);
    };

    const onKeyUp = (event: KeyboardEvent) => {
      const index = held.indexOf(event.code);
      if (index === -1) return;

      held.splice(index, 1);

      // Releasing one key while another is still held should fall back to that key rather than
      // stopping - otherwise fast direction changes stutter.
      const fallback = held.length > 0 ? directionForKey(held[held.length - 1]) : null;
      sendInput(fallback ?? 'None');
    };

    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);

    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
    };
  }, [sendInput, enabled]);
}
