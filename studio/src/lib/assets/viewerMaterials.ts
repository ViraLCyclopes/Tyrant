import type { ModSampledColors, PreviewMaterial } from '$lib/rpc/types.gen';

/** How the viewer dresses one material: animals get the PK animal plugin with their data maps; others keep their textures. */
export interface MaterialPlan {
  name: string;
  kind: 'animal' | 'standard';
  cutoff: number | null;
  /** Files (not URLs) of the animal data maps; null when the material has none. */
  extra: string | null;
  pattern: string | null;
}

export function planMaterial(m: PreviewMaterial): MaterialPlan {
  const file = (slot: string) => m.slots?.find((s) => s.name === slot)?.file ?? null;
  return m.animal
    ? { name: m.name, kind: 'animal', cutoff: m.cutoff ?? 0.5, extra: file('_AdultExtraMap'), pattern: file('_AdultPatternMask') }
    : { name: m.name, kind: 'standard', cutoff: m.cutoff ?? null, extra: null, pattern: null };
}

type Vec3 = [number, number, number];

/** The PK animal plugin's inputs; colours are sRGB 0–1, as the core's ColorPreview uses them. */
export interface AnimalUniforms {
  colorA: Vec3;
  colorB: Vec3;
  secondary: Vec3;
  eye: Vec3;
  /** has A/B, has secondary, has eye, strength */
  flags: [number, number, number, number];
  /** softness, hue, saturation, value */
  shape: [number, number, number, number];
}

const BLACK: Vec3 = [0, 0, 0];

function rgb(hex: string | null | undefined): Vec3 | null {
  if (!hex || !/^#[0-9a-f]{6}$/i.test(hex)) return null;
  return [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255) as Vec3;
}

/** One animal's colours as shader inputs; null leaves the textures untouched (strength 0). */
export function animalUniforms(colors: ModSampledColors | null): AnimalUniforms {
  const b = rgb(colors?.b) ?? rgb(colors?.a);
  const a = rgb(colors?.a) ?? b;
  const secondary = rgb(colors?.secondary);
  const eye = rgb(colors?.eye);
  return {
    colorA: a ?? BLACK,
    colorB: b ?? BLACK,
    secondary: secondary ?? BLACK,
    eye: eye ?? BLACK,
    flags: [a ? 1 : 0, secondary ? 1 : 0, eye ? 1 : 0, colors ? colors.strength : 0],
    shape: [Math.max(0.01, colors?.softness ?? 0.25), colors?.hue ?? 0, colors?.saturation ?? 0, colors?.value ?? 0],
  };
}
