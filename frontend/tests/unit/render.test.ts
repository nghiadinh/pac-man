import { describe, expect, it } from 'vitest';
import { directionForKey } from '../../src/hooks/useKeyboardInput';
import { boardPixelSize, centerOf, TILE_SIZE } from '../../src/render/boardRenderer';
import type { MatchStateDto } from '../../src/net/matchConnection';

describe('directionForKey', () => {
  it('maps the arrow keys', () => {
    expect(directionForKey('ArrowUp')).toBe('Up');
    expect(directionForKey('ArrowDown')).toBe('Down');
    expect(directionForKey('ArrowLeft')).toBe('Left');
    expect(directionForKey('ArrowRight')).toBe('Right');
  });

  it('maps WASD to the same directions', () => {
    expect(directionForKey('KeyW')).toBe('Up');
    expect(directionForKey('KeyS')).toBe('Down');
    expect(directionForKey('KeyA')).toBe('Left');
    expect(directionForKey('KeyD')).toBe('Right');
  });

  it('ignores keys that are not movement', () => {
    expect(directionForKey('Space')).toBeNull();
    expect(directionForKey('Escape')).toBeNull();
    expect(directionForKey('KeyQ')).toBeNull();
  });
});

describe('centerOf', () => {
  it('returns the pixel centre of a tile', () => {
    expect(centerOf(0)).toBe(TILE_SIZE / 2);
    expect(centerOf(3)).toBe(3 * TILE_SIZE + TILE_SIZE / 2);
  });

  it('handles fractional tile positions mid-traversal', () => {
    // Players sit between tile centres while moving, so this must not round.
    expect(centerOf(2.5)).toBe(2.5 * TILE_SIZE + TILE_SIZE / 2);
  });
});

describe('boardPixelSize', () => {
  it('scales the board by the tile size', () => {
    const state = {
      map: { width: 28, height: 24 },
    } as MatchStateDto;

    expect(boardPixelSize(state)).toEqual({
      width: 28 * TILE_SIZE,
      height: 24 * TILE_SIZE,
    });
  });
});
