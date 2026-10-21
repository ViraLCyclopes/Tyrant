import { describe, expect, it } from 'vitest';
import { extremes, fade, rebuild, speciesExtremes, type Key } from './sizeMath';

describe('sizeMath (the card’s copy of the game’s size maths)', () => {
  it('counts the fixture SizeMathTests.Extremes_pinned_for_the_size_cards_copy pins', () => {
    const curve: Key[] = [[0, 0.25], [1, 1]];
    const sexes = [1.1 * 2, 1.0 * 2 * 1.2];
    const { min, max } = extremes(rebuild(curve, 2, 0.1), sexes, [[0.925, 1.075], [0.5, 3], [0.85, 1.15]]);
    expect(min).toBeCloseTo(0.17, 5);
    expect(max).toBeCloseTo(7.2, 4);
  });

  it('keeps the adult at adult × shape and the newborn at its share', () => {
    expect(fade(0)).toBeCloseTo(0.2);
    expect(fade(1)).toBe(1);
    const keys = rebuild([[0, 0.5], [1, 1]], 3, 0.2);
    expect(keys).toHaveLength(17);
    expect(keys[16][1]).toBeCloseTo(1); // at s·g = adult the size is adult × shape, so the curve's adult value stays
    expect(keys[0][1]).toBeCloseTo((3 * 0.2) / (1 + 2 * 0.2));
  });

  it('counts every skin, the species’ ranges or the game’s, and nothing without a curve', () => {
    const base = {
      curve: [[0, 0.25], [1, 1]] as Key[], skins: [{ male: 1.1, female: 1 }], adult: 2, baby: 0.1, male: null, female: 1.2,
      nursery: [0.5, 3] as [number, number], breeding: null, gameIncubation: [0.925, 1.075] as [number, number], gameBreeding: [0.85, 1.15] as [number, number],
    };
    const found = speciesExtremes(base)!;
    expect(found.min).toBeCloseTo(0.17, 5);
    expect(found.max).toBeCloseTo(7.2, 4);
    expect(speciesExtremes({ ...base, nursery: null })!.max).toBeCloseTo(2.4 * 1.15, 4);
    expect(speciesExtremes({ ...base, curve: [] })).toBeNull();
  });
});
