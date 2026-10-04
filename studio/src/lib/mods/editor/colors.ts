/** The "colors" section of a skin in mod.json, as the colour editor edits it. Unset (null) keys are left out of mod.json. */
export type Variant = 'normal' | 'albino' | 'melanistic' | 'leucistic';

export interface Range {
  min: number;
  max: number;
}

export interface TintModel {
  hue: Range | null;
  saturation: Range | null;
  value: Range | null;
}

export interface SetModel {
  a: string[] | null;
  b: string[] | null;
  secondary: string[] | null;
  eye: string[] | null;
  strength: Range | null;
  softness: Range | null;
  /** Mutations only; normal animals use ColorsModel.tint. */
  tint: TintModel | null;
}

export interface ColorsModel {
  tint: TintModel | null;
  pattern: SetModel | null;
  albino: SetModel | null;
  melanistic: SetModel | null;
  leucistic: SetModel | null;
}

export type SetKey = 'pattern' | 'albino' | 'melanistic' | 'leucistic';

export const MAX_STOPS = 8;

export const VARIANTS: { id: Variant; label: string; key: SetKey }[] = [
  { id: 'normal', label: 'Normal', key: 'pattern' },
  { id: 'albino', label: 'Albino', key: 'albino' },
  { id: 'melanistic', label: 'Melanistic', key: 'melanistic' },
  { id: 'leucistic', label: 'Leucistic', key: 'leucistic' },
];

export function emptySet(): SetModel {
  return { a: null, b: null, secondary: null, eye: null, strength: null, softness: null, tint: null };
}

export function hasColour(set: SetModel | null): boolean {
  return !!set && !!((set.a && set.a.length) || (set.b && set.b.length));
}

type Json = Record<string, unknown>;

export function parseColors(json: string | null): ColorsModel {
  const root = (json ? JSON.parse(json) : {}) as Json;
  return {
    tint: readTint(root.tint),
    pattern: readSet(root.pattern),
    albino: readSet(root.albino),
    melanistic: readSet(root.melanistic),
    leucistic: readSet(root.leucistic),
  };
}

export function toColorsJson(model: ColorsModel): string | null {
  const out: Json = {};
  const tint = writeTint(model.tint);
  if (tint) out.tint = tint;
  const pattern = hasColour(model.pattern) ? writeSet(model.pattern) : null; // the game needs a colour in "pattern"
  if (pattern) out.pattern = pattern;
  for (const key of ['albino', 'melanistic', 'leucistic'] as const) {
    const set = writeSet(model[key]);
    if (set) out[key] = set;
  }
  return Object.keys(out).length ? JSON.stringify(out) : null;
}

function readColours(value: unknown): string[] | null {
  if (typeof value === 'string') return [value.toLowerCase()];
  return Array.isArray(value) && value.length ? value.map((v) => String(v).toLowerCase()) : null;
}

function readRange(value: unknown): Range | null {
  if (typeof value === 'number') return { min: value, max: value };
  return Array.isArray(value) && value.length === 2 ? { min: Number(value[0]), max: Number(value[1]) } : null;
}

function readTint(value: unknown): TintModel | null {
  if (!value || typeof value !== 'object') return null;
  const t = value as Json;
  return { hue: readRange(t.hue), saturation: readRange(t.saturation), value: readRange(t.value) };
}

function readSet(value: unknown): SetModel | null {
  if (!value || typeof value !== 'object') return null;
  const s = value as Json;
  return {
    a: readColours(s.a),
    b: readColours(s.b),
    secondary: readColours(s.secondary),
    eye: readColours(s.eye),
    strength: readRange(s.strength),
    softness: readRange(s.softness),
    tint: readTint(s.tint),
  };
}

function writeColours(colours: string[] | null): unknown {
  if (!colours || !colours.length) return undefined;
  const lower = colours.slice(0, MAX_STOPS).map((c) => c.toLowerCase());
  return lower.length === 1 ? lower[0] : lower;
}

function writeRange(range: Range | null): unknown {
  if (!range) return undefined;
  return range.min === range.max ? range.min : [range.min, range.max];
}

function writeTint(tint: TintModel | null): Json | null {
  if (!tint) return null;
  const out = compact({ hue: writeRange(tint.hue), saturation: writeRange(tint.saturation), value: writeRange(tint.value) });
  return Object.keys(out).length ? out : null;
}

function writeSet(set: SetModel | null): Json | null {
  if (!set) return null;
  const out = compact({
    a: writeColours(set.a),
    b: writeColours(set.b),
    strength: writeRange(set.strength),
    softness: writeRange(set.softness),
    secondary: writeColours(set.secondary),
    eye: writeColours(set.eye),
    tint: writeTint(set.tint) ?? undefined,
  });
  return Object.keys(out).length ? out : null;
}

function compact(map: Json): Json {
  return Object.fromEntries(Object.entries(map).filter(([, v]) => v !== undefined));
}
