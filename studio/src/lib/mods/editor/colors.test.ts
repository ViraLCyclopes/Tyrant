import { describe, expect, it } from 'vitest';
import { emptySet, hasColour, parseColors, toColorsJson } from './colors';

describe('colours model', () => {
  it('reads single colours, gradients, ranges and fixed numbers', () => {
    const m = parseColors('{"tint":{"hue":[-0.05,0.05],"value":0},"pattern":{"a":["#3060ff","#2040c0"],"b":"#20c040","strength":[0.6,0.8]}}');
    expect(m.tint).toEqual({ hue: { min: -0.05, max: 0.05 }, saturation: null, value: { min: 0, max: 0 } });
    expect(m.pattern?.a).toEqual(['#3060ff', '#2040c0']);
    expect(m.pattern?.b).toEqual(['#20c040']);
    expect(m.pattern?.strength).toEqual({ min: 0.6, max: 0.8 });
    expect(m.albino).toBeNull();
  });

  it('writes one colour as a string, a fixed range as a number, and leaves unset keys out', () => {
    const m = parseColors(null);
    m.pattern = { ...emptySet(), a: ['#3060FF'], strength: { min: 0.7, max: 0.7 }, softness: { min: 0.1, max: 0.35 } };
    expect(JSON.parse(toColorsJson(m)!)).toEqual({ pattern: { a: '#3060ff', strength: 0.7, softness: [0.1, 0.35] } });
  });

  it('drops empty sets and returns null when nothing is left', () => {
    const m = parseColors('{"albino":{"a":"#ffffff"}}');
    m.albino = emptySet();
    expect(toColorsJson(m)).toBeNull();
  });

  it('a normal pattern without a colour is dropped (the game needs one)', () => {
    const m = parseColors(null);
    m.pattern = { ...emptySet(), strength: { min: 0.5, max: 0.5 } };
    expect(toColorsJson(m)).toBeNull();
    expect(hasColour(m.pattern)).toBe(false);
  });

  it('keeps a mutation set with only a tint', () => {
    const m = parseColors(null);
    m.melanistic = { ...emptySet(), tint: { hue: null, saturation: null, value: { min: -0.2, max: -0.1 } } };
    expect(JSON.parse(toColorsJson(m)!)).toEqual({ melanistic: { tint: { value: [-0.2, -0.1] } } });
  });

  it('round-trips the documented example', () => {
    const json =
      '{"tint":{"hue":[-0.05,0.05],"saturation":0,"value":0},"pattern":{"a":["#3060ff","#2040c0"],"b":"#20c040","strength":[0.6,0.8],"softness":[0.1,0.35],"secondary":"#ffcc00","eye":"#ff2000"},"albino":{"a":"#ffffff","eye":"#ff4060"}}';
    expect(JSON.parse(toColorsJson(parseColors(json))!)).toEqual(JSON.parse(json));
  });
});
