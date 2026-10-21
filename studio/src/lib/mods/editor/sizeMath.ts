/**
 * The game's size maths, as Tyrant counts it (Framework.Core SizeMath and SizePlan), so the Size card shows a species' smallest
 * and biggest while a slider is dragged, before anything is saved. The final size is S(m, s, g) = curve(m) · (1 + (s·g − 1)·f(m)),
 * with m the maturity, s the sex size (the adult multiplier folded in) and g the genetic size. SizeMathTests pins the same numbers.
 */

export type Key = [number, number];
export type Range = [number, number];

const STEPS = 16;
const SAMPLES = 101;

/** The game's maturity fade f(m) = 0.2 + 0.8·(1 − (1 − m)²), m clamped to 0–1. */
export function fade(maturity: number): number {
  const m = Math.min(1, Math.max(0, maturity));
  const inv = 1 - m;
  return 0.2 + 0.8 * (1 - inv * inv);
}

/** The curve at t, linear between its keys (the server sends the game's curve sampled). */
export function evaluate(keys: Key[], t: number): number {
  if (keys.length === 0) return 0;
  if (t <= keys[0][0]) return keys[0][1];
  for (let i = 1; i < keys.length; i++) {
    if (t > keys[i][0]) continue;
    const [t0, v0] = keys[i - 1];
    const [t1, v1] = keys[i];
    return t1 <= t0 ? v1 : v0 + ((v1 - v0) * (t - t0)) / (t1 - t0);
  }
  return keys[keys.length - 1][1];
}

/** The growth curve for an adult multiplier and hatchling share (SizeMath.Rebuild): 17 keys over 0–1 and the original's past 1. */
export function rebuild(original: Key[], adult: number, baby: number | null): Key[] {
  const c0 = evaluate(original, 0);
  const c1 = evaluate(original, 1);
  const share0 = c1 === 0 ? 1 : c0 / c1;
  let a = 0;
  let b = 1;
  if (1 - share0 >= 1e-4) {
    const target = baby ?? share0;
    b = (c1 * (1 - target)) / (c1 - c0);
    a = c1 - b * c1;
  }
  const shape = (v: number) => a + b * v;
  const keys: Key[] = [];
  for (let i = 0; i <= STEPS; i++) {
    const m = i / STEPS;
    keys.push([m, (adult * shape(evaluate(original, m))) / (1 + (adult - 1) * fade(m))]);
  }
  for (const [t, v] of original.filter((k) => k[0] > 1).sort((x, y) => x[0] - y[0])) keys.push([t, shape(v)]);
  return keys;
}

/** The smallest and largest final size over maturity (101 samples), the sex sizes and the genetic ranges' ends (SizeMath.Extremes). */
export function extremes(curve: Key[], sexes: number[], ranges: Range[]): { min: number; max: number } {
  const genetics = ranges.flatMap((r) => [r[0], r[1]]);
  let min = Infinity;
  let max = -Infinity;
  for (let i = 0; i < SAMPLES; i++) {
    const m = i / (SAMPLES - 1);
    const at = evaluate(curve, m);
    const f = fade(m);
    for (const s of sexes)
      for (const g of genetics) {
        const size = at * (1 + (s * g - 1) * f);
        if (size < min) min = size;
        if (size > max) max = size;
      }
  }
  return { min, max };
}

export type SizeInputs = {
  curve: Key[];
  skins: { male: number; female: number }[];
  adult: number | null;
  baby: number | null;
  male: number | null;
  female: number | null;
  nursery: Range | null;
  breeding: Range | null;
  gameIncubation: Range;
  gameBreeding: Range;
};

/**
 * A species' smallest and largest as Check counts them (SpeciesSizes.Extremes): every skin's sexes with the adult folded in,
 * random eggs' range, the Nursery slider's (its own, else the eggs') and breeding's (its own, else the game's). Null without a curve.
 */
export function speciesExtremes(s: SizeInputs): { min: number; max: number } | null {
  if (s.curve.length === 0) return null;
  const adult = s.adult ?? 1;
  const skins = s.skins.length === 0 ? [{ male: 1, female: 1 }] : s.skins;
  const sexes = skins.flatMap((k) => [k.male * adult * (s.male ?? 1), k.female * adult * (s.female ?? 1)]);
  const ranges: Range[] = [];
  for (const r of [s.gameIncubation, s.nursery ?? s.gameIncubation, s.breeding ?? s.gameBreeding])
    if (!ranges.some((x) => x[0] === r[0] && x[1] === r[1])) ranges.push(r);
  return extremes(rebuild(s.curve, adult, s.baby), sexes, ranges);
}
