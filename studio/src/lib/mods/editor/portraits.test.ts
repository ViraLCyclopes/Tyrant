import { describe, expect, it } from 'vitest';
import type { ModPortraitDto } from '$lib/rpc/types.gen';
import { PORTRAIT_SLOTS, isTwoToOne, renderTargets, slotOf } from './portraits';

const row = (slot: string, own: boolean, rendered: boolean): ModPortraitDto => ({ slot, file: own ? `C:/p/${slot}.png` : null, own, rendered, width: 128, height: 64 });

describe('portraits', () => {
  it('names the grid cells like the game', () => {
    expect(PORTRAIT_SLOTS).toHaveLength(12);
    expect(slotOf(0, 0)).toBe('adultMale');
    expect(slotOf(1, 2)).toBe('adultFemaleMelanistic');
    expect(slotOf(2, 3)).toBe('infantLeucistic');
  });

  it('renders over empty and rendered slots but keeps hand-picked ones unless asked', () => {
    const shown = [row('adultMale', true, false), row('adultFemale', true, true), row('infant', false, false)];
    expect(renderTargets(shown, ['male', 'female', 'infant'], false)).toEqual(['female', 'infant']);
    expect(renderTargets(shown, ['male', 'female', 'infant'], true)).toEqual(['male', 'female', 'infant']);
    expect(renderTargets(shown, ['male', 'infant'], false)).toEqual(['infant']); // a male-only skin has no female to render
  });

  it('allows 2% off 2:1', () => {
    expect(isTwoToOne(128, 64)).toBe(true);
    expect(isTwoToOne(130, 64)).toBe(true);
    expect(isTwoToOne(64, 64)).toBe(false);
    expect(isTwoToOne(0, 0)).toBe(false);
  });
});
