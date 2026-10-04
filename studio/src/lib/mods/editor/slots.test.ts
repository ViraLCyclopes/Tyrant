import { describe, expect, it } from 'vitest';
import { checkLinesFor, isColourSlot, slotsFor } from './slots';

describe('skin slots', () => {
  it("shows the base skin's slots in the game's order, plus extra ones the skin has", () => {
    expect(slotsFor({ diffuse: 'a', fur: 'b' }, ['normal', 'diffuse', 'pattern'])).toEqual(['diffuse', 'normal', 'pattern', 'fur']);
  });

  it('without the game data, shows the usual four plus what the skin has', () => {
    expect(slotsFor({ infantDiffuse: 'x' }, null)).toEqual(['diffuse', 'normal', 'extra', 'pattern', 'infantDiffuse']);
  });

  it('colour slots are the diffuse ones', () => {
    expect(isColourSlot('diffuse')).toBe(true);
    expect(isColourSlot('infantDiffuse')).toBe(true);
    expect(isColourSlot('pattern')).toBe(false);
  });

  it("picks this skin's Check lines by its key", () => {
    const lines = checkLinesFor(
      { errors: ['red-spot/blue male diffuse: missing', 'red-spot/bluish: x'], warnings: ['red-spot/blue: y', 'T_A_D: z'], missingCutouts: [] },
      'red-spot/blue',
    );
    expect(lines).toEqual({ errors: ['red-spot/blue male diffuse: missing'], warnings: ['red-spot/blue: y'] });
  });
});
