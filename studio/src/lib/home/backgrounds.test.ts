import { describe, expect, it } from 'vitest';
import { BACKGROUNDS, rotation } from './backgrounds';

describe('start-screen backgrounds', () => {
  it('opens on the sauropods, then shows every other picture once in a shuffled order', () => {
    const values = [0.9, 0.1, 0.5, 0.7, 0.3, 0.2, 0.8, 0.4, 0.6];
    let i = 0;
    const order = rotation(BACKGROUNDS, () => values[i++ % values.length]);

    expect(order[0]).toBe('/hero.jpg');
    expect([...order].sort()).toEqual([...BACKGROUNDS].sort());
    expect(order.slice(1)).not.toEqual(BACKGROUNDS.slice(1)); // shuffled
  });

  it('lists the sauropods and nine screenshots', () => {
    expect(BACKGROUNDS[0]).toBe('/hero.jpg');
    expect(BACKGROUNDS).toHaveLength(10);
    expect(BACKGROUNDS.slice(1).every((b) => /^\/backgrounds\/bg-\d\d\.jpg$/.test(b))).toBe(true);
  });
});
