/**
 * One material's block of a skin's or species' "materials" in mod.json (keyed by material name), as the Material card edits
 * it. Unset (null, or "game") values are left out of mod.json; keys the editor does not know are dropped when parsing.
 */
export type MaskSlot = 'extra' | 'pattern' | 'fur';
export type Channel = 'r' | 'g' | 'b' | 'a';

export type RampModel = { kind: 'game' } | { kind: 'file'; file: string } | { kind: 'stops'; stops: string[] } | { kind: 'thinFilm'; thickness: number; index: number };

export type MaskModel =
  | { kind: 'game' }
  | { kind: 'everywhere' }
  | { kind: 'file'; file: string }
  | { kind: 'from'; slot: MaskSlot; channel: Channel; invert: boolean };

export interface StrengthModel {
  normal: number;
  albino?: number;
  melanistic?: number;
  leucistic?: number;
}

export interface IridescenceModel {
  strength: StrengthModel | null;
  variation: number | null;
  ramp: RampModel;
  mask: MaskModel;
  scale: number | null;
  power: number | null;
  bias: number | null;
  /** The mask's tiling and offset (`_IridescenceMask_ST`): kept as they are, the card has no fields for them. */
  tiling?: number[];
  offset?: number[];
}

export interface MaterialModel {
  iridescence: IridescenceModel | null;
  settings: Record<string, number | string | number[]>;
}

export const MAX_RAMP_STOPS = 32;
export const DEFAULT_SHAPING = { scale: -0.25, power: -0.3, bias: 1.3 };
export const DEFAULT_THICKNESS = 420;
export const DEFAULT_INDEX = 1.56;
export const DEFAULT_VARIATION = 0.15;
export const MUTATIONS = ['albino', 'melanistic', 'leucistic'] as const;
export const MASK_SLOTS: MaskSlot[] = ['extra', 'pattern', 'fur'];
export const CHANNELS: Channel[] = ['r', 'g', 'b', 'a'];

type Json = Record<string, unknown>;

export function emptyIridescence(): IridescenceModel {
  return { strength: null, variation: null, ramp: { kind: 'game' }, mask: { kind: 'game' }, scale: null, power: null, bias: null };
}

export function parseMaterial(json: string | null, name: string): MaterialModel {
  const root = asObject(json ? JSON.parse(json) : null);
  const block = asObject(root?.[name]);
  return { iridescence: readIridescence(block?.iridescence), settings: readSettings(block?.settings) };
}

/** An "iridescence" block on its own (mods.materialInfo's inherited one). */
export function parseIridescenceJson(json: string | null): IridescenceModel | null {
  return json ? readIridescence(JSON.parse(json)) : null;
}

/** `{ name: block }`, or null when the block is empty. */
export function toMaterialsJson(name: string, model: MaterialModel): string | null {
  const block = writeBlock(model);
  return block ? JSON.stringify({ [name]: block }) : null;
}

/** The whole "materials" JSON with this material's block replaced (other materials kept); null when nothing is left. */
export function replaceMaterial(saved: string | null, name: string, model: MaterialModel): string | null {
  const root: Json = { ...(asObject(saved ? JSON.parse(saved) : null) ?? {}) };
  delete root[name];
  const block = writeBlock(model);
  if (block) root[name] = block;
  return Object.keys(root).length ? JSON.stringify(root) : null;
}

/** A plain deep copy (also of Svelte state, which structuredClone refuses). */
export function cloneMaterial(model: MaterialModel): MaterialModel {
  return JSON.parse(JSON.stringify(model)) as MaterialModel;
}

/** The highest strength of any variant (a variant left out uses normal). */
export function maxStrength(s: StrengthModel): number {
  return Math.max(s.normal, ...MUTATIONS.map((m) => s[m] ?? s.normal));
}

function asObject(value: unknown): Json | null {
  return value && typeof value === 'object' && !Array.isArray(value) ? (value as Json) : null;
}

const num = (value: unknown): number | null => (typeof value === 'number' && Number.isFinite(value) ? value : null);

function numbers(value: unknown, min: number, max: number): number[] | undefined {
  return Array.isArray(value) && value.length >= min && value.length <= max && value.every((v) => num(v) !== null) ? (value as number[]) : undefined;
}

function readIridescence(value: unknown): IridescenceModel | null {
  const i = asObject(value);
  if (!i) return null;
  const tiling = numbers(i.tiling, 2, 2);
  const offset = numbers(i.offset, 2, 2);
  return {
    strength: readStrength(i.strength),
    variation: num(i.variation),
    ramp: readRamp(i.ramp),
    mask: readMask(i.mask),
    scale: num(i.scale),
    power: num(i.power),
    bias: num(i.bias),
    ...(tiling ? { tiling } : {}),
    ...(offset ? { offset } : {}),
  };
}

function readStrength(value: unknown): StrengthModel | null {
  if (num(value) !== null) return { normal: value as number };
  const s = asObject(value);
  if (!s || num(s.normal) === null) return null;
  const out: StrengthModel = { normal: s.normal as number };
  for (const m of MUTATIONS) if (num(s[m]) !== null) out[m] = s[m] as number;
  return out;
}

function readRamp(value: unknown): RampModel {
  if (typeof value === 'string' && value) return { kind: 'file', file: value };
  const r = asObject(value);
  if (Array.isArray(r?.stops) && r.stops.length && r.stops.every((c) => typeof c === 'string')) return { kind: 'stops', stops: (r.stops as string[]).map((c) => c.toLowerCase()) };
  const film = asObject(r?.thinFilm);
  if (film && num(film.thickness) !== null) return { kind: 'thinFilm', thickness: film.thickness as number, index: num(film.index) ?? DEFAULT_INDEX };
  return { kind: 'game' };
}

function readMask(value: unknown): MaskModel {
  if (value === 'everywhere') return { kind: 'everywhere' };
  if (typeof value === 'string' && value) return { kind: 'file', file: value };
  const m = asObject(value);
  if (!m || !MASK_SLOTS.includes(m.from as MaskSlot)) return { kind: 'game' };
  const channel = typeof m.channel === 'string' ? (m.channel.toLowerCase() as Channel) : 'r';
  return { kind: 'from', slot: m.from as MaskSlot, channel: CHANNELS.includes(channel) ? channel : 'r', invert: m.invert === true };
}

function readSettings(value: unknown): MaterialModel['settings'] {
  const out: MaterialModel['settings'] = {};
  for (const [key, v] of Object.entries(asObject(value) ?? {})) {
    if (num(v) !== null) out[key] = v as number;
    else if (typeof v === 'string') out[key] = v.toLowerCase();
    else if (numbers(v, 2, 4)) out[key] = v as number[];
  }
  return out;
}

function writeBlock(model: MaterialModel): Json | null {
  const block: Json = {};
  const iridescence = model.iridescence ? writeIridescence(model.iridescence) : null;
  if (iridescence) block.iridescence = iridescence;
  if (Object.keys(model.settings).length) block.settings = { ...model.settings };
  return Object.keys(block).length ? block : null;
}

function writeIridescence(i: IridescenceModel): Json | null {
  const out: Json = {};
  if (i.strength) out.strength = writeStrength(i.strength);
  if (i.variation !== null) out.variation = i.variation;
  const ramp = writeRamp(i.ramp);
  if (ramp !== undefined) out.ramp = ramp;
  const mask = writeMask(i.mask);
  if (mask !== undefined) out.mask = mask;
  if (i.tiling) out.tiling = [...i.tiling];
  if (i.offset) out.offset = [...i.offset];
  if (i.scale !== null) out.scale = i.scale;
  if (i.power !== null) out.power = i.power;
  if (i.bias !== null) out.bias = i.bias;
  return Object.keys(out).length ? out : null;
}

/** A variant equal to normal is left out (it uses normal anyway); with none left, one number. */
function writeStrength(s: StrengthModel): number | Json {
  const differ = MUTATIONS.filter((m) => s[m] !== undefined && s[m] !== s.normal);
  if (!differ.length) return s.normal;
  return { normal: s.normal, ...Object.fromEntries(differ.map((m) => [m, s[m]])) };
}

function writeRamp(r: RampModel): unknown {
  switch (r.kind) {
    case 'file':
      return r.file;
    case 'stops':
      return { stops: r.stops.slice(0, MAX_RAMP_STOPS).map((c) => c.toLowerCase()) };
    case 'thinFilm':
      return { thinFilm: r.index === DEFAULT_INDEX ? { thickness: r.thickness } : { thickness: r.thickness, index: r.index } };
    default:
      return undefined;
  }
}

function writeMask(m: MaskModel): unknown {
  switch (m.kind) {
    case 'everywhere':
      return 'everywhere';
    case 'file':
      return m.file;
    case 'from':
      return { from: m.slot, channel: m.channel, invert: m.invert };
    default:
      return undefined;
  }
}
