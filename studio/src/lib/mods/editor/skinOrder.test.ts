import { describe, expect, it } from 'vitest';
import { moveBy, moveTo } from './skinOrder';

describe('skin picker order', () => {
  it('moveTo puts a skin before the one now at that place, or at the end', () => {
    expect(moveTo(['a', 'b', 'c'], 'c', 0)).toEqual(['c', 'a', 'b']);
    expect(moveTo(['a', 'b', 'c'], 'a', 3)).toEqual(['b', 'c', 'a']);
    expect(moveTo(['a', 'b', 'c'], 'a', 2)).toEqual(['b', 'a', 'c']);
  });

  it('moveTo is null when nothing changes or the skin is not there', () => {
    expect(moveTo(['a', 'b', 'c'], 'b', 1)).toBeNull();
    expect(moveTo(['a', 'b', 'c'], 'b', 2)).toBeNull();
    expect(moveTo(['a', 'b', 'c'], 'x', 0)).toBeNull();
  });

  it('moveBy swaps a skin with its neighbour; null at either end', () => {
    expect(moveBy(['a', 'b'], 'a', -1)).toBeNull();
    expect(moveBy(['a', 'b'], 'b', 1)).toBeNull();
    expect(moveBy(['a', 'b'], 'a', 1)).toEqual(['b', 'a']);
    expect(moveBy(['a', 'b', 'c'], 'c', -1)).toEqual(['a', 'c', 'b']);
    expect(moveBy(['a', 'b'], 'x', 1)).toBeNull();
  });
});
