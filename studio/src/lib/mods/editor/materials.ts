/**
 * One material's block of a skin's or species' "materials" in mod.json (keyed by material name), as the Material card edits
 * it. Unset (null, or "inherit") values are left out of mod.json; keys the editor does not know are dropped when parsing.
 * "game" is the explicit `"game"` value: the game's own ramp or mask, over whatever a lower level sets. "species" names a
 * vanilla species whose ramp or mask is taken (`{ "species": id }`): a reference, never a copy of the game's image.
 */
export type MaskSlot = 'extra' | 'pattern' | 'fur';
export type Channel = 'r' | 'g' | 'b' | 'a';

/** The explicit "the game's own" value of a ramp, a mask or a fur mask. */
export const GAME = 'game';

export type RampModel =
  | { kind: 'inherit' }
  | { kind: 'game' }
  | { kind: 'species'; species: string }
  | { kind: 'file'; file: string }
  | { kind: 'stops'; stops: string[] }
  | { kind: 'thinFilm'; thickness: number; index: number };

export type MaskModel =
  | { kind: 'inherit' }
  | { kind: 'game' }
  | { kind: 'species'; species: string }
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
  /**
   * A hatchling's strength (one number for every variant): the shown strength slides from it to `strength` as the animal grows.
   * Null leaves it to the levels below (the game's own is 0); 0 is kept, over a lower level's baby shimmer.
   */
  babyStrength: number | null;
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
  /**
   * Fur masks taken from files (mod-relative paths, or `"game"`: the game's own): the adult one and the baby one. `grow` true turns the game's fur on for
   * skins without any; false says this level does not (over a lower one that does); left out, the levels below decide. `age`
   * picks which ages wear fur (Tyrant grows it where an age has no fur map); left out, the game's fur maps decide.
   */
  fur?: { mask?: string; infantMask?: string; grow?: boolean; age?: FurAge };
}

/** "fur.age": which ages wear fur. */
export type FurAge = 'babies' | 'adults' | 'both';
export const FUR_AGES: FurAge[] = ['babies', 'adults', 'both'];

/** The fur length growing fur starts with when nothing gives the fur any (the shader's `_Length` runs 0 to 4). */
export const START_FUR_LENGTH = 0.5;

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
  return { strength: null, babyStrength: null, variation: null, ramp: { kind: 'inherit' }, mask: { kind: 'inherit' }, scale: null, power: null, bias: null };
}

export function parseMaterial(json: string | null, name: string): MaterialModel {
  const root = asObject(json ? JSON.parse(json) : null);
  const block = asObject(root?.[name]);
  const fur = readFur(block?.fur);
  return { iridescence: readIridescence(block?.iridescence), settings: readSettings(block?.settings), ...(fur ? { fur } : {}) };
}

/** An "iridescence" block on its own (mods.materialInfo's inherited one). */
export function parseIridescenceJson(json: string | null): IridescenceModel | null {
  return json ? readIridescence(JSON.parse(json)) : null;
}

/** A "fur" block on its own (mods.materialInfo's inherited one); null when there is none. */
export function parseFurJson(json: string | null | undefined): MaterialModel['fur'] | null {
  return json ? readFur(JSON.parse(json)) : null;
}

/** True for a ramp or mask a level actually picks (not left to the levels below, not the game's own). */
export const picked = (part: RampModel | MaskModel | undefined): boolean => !!part && part.kind !== 'inherit' && part.kind !== 'game';

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

/**
 * Merges a species preset's block (mods.speciesPreset's materialJson, one material's entry) into a model: what the preset sets
 * replaces this level's (iridescence fields, settings, fur.grow), everything else stays, as the in-game editor's Apply does.
 * A block without a strength (the species' is not known) turns iridescence on: a strength of 0 everywhere, or none, becomes 1.
 */
export function applyPresetBlock(model: MaterialModel, blockJson: string): void {
  const block = asObject(JSON.parse(blockJson));
  const preset = readIridescence(block?.iridescence);
  if (preset) {
    const i = (model.iridescence ??= emptyIridescence());
    if (preset.strength) i.strength = preset.strength;
    else if (!i.strength || maxStrength(i.strength) <= 0)
      i.strength = { normal: 1, ...Object.fromEntries(MUTATIONS.filter((m) => i.strength?.[m] !== undefined).map((m) => [m, 1])) };
    if (preset.variation !== null) i.variation = preset.variation;
    if (preset.ramp.kind !== 'inherit') i.ramp = preset.ramp;
    if (preset.mask.kind !== 'inherit') i.mask = preset.mask;
    if (preset.scale !== null) i.scale = preset.scale;
    if (preset.power !== null) i.power = preset.power;
    if (preset.bias !== null) i.bias = preset.bias;
  }
  Object.assign(model.settings, readSettings(block?.settings));
  const fur = readFur(block?.fur);
  if (fur?.grow !== undefined) model.fur = { ...(model.fur ?? {}), grow: fur.grow };
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
    babyStrength: num(i.babyStrength),
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

/** `{ "species": id }`'s id; null for anything else. */
function speciesOf(value: Json | null): string | null {
  return typeof value?.species === 'string' && value.species.trim() ? value.species : null;
}

function readRamp(value: unknown): RampModel {
  if (value === GAME) return { kind: 'game' };
  if (typeof value === 'string' && value) return { kind: 'file', file: value };
  const r = asObject(value);
  const species = speciesOf(r);
  if (species) return { kind: 'species', species };
  if (Array.isArray(r?.stops) && r.stops.length && r.stops.every((c) => typeof c === 'string')) return { kind: 'stops', stops: (r.stops as string[]).map((c) => c.toLowerCase()) };
  const film = asObject(r?.thinFilm);
  if (film && num(film.thickness) !== null) return { kind: 'thinFilm', thickness: film.thickness as number, index: num(film.index) ?? DEFAULT_INDEX };
  return { kind: 'inherit' };
}

function readMask(value: unknown): MaskModel {
  if (value === GAME) return { kind: 'game' };
  if (value === 'everywhere') return { kind: 'everywhere' };
  if (typeof value === 'string' && value) return { kind: 'file', file: value };
  const m = asObject(value);
  const species = speciesOf(m);
  if (species) return { kind: 'species', species };
  if (!m || !MASK_SLOTS.includes(m.from as MaskSlot)) return { kind: 'inherit' };
  const channel = typeof m.channel === 'string' ? (m.channel.toLowerCase() as Channel) : 'r';
  return { kind: 'from', slot: m.from as MaskSlot, channel: CHANNELS.includes(channel) ? channel : 'r', invert: m.invert === true };
}

function readFur(value: unknown): MaterialModel['fur'] | null {
  const f = asObject(value);
  const mask = typeof f?.mask === 'string' && f.mask ? f.mask : undefined;
  const infantMask = typeof f?.infantMask === 'string' && f.infantMask ? f.infantMask : undefined;
  const grow = typeof f?.grow === 'boolean' ? f.grow : undefined;
  const age = FUR_AGES.includes(f?.age as FurAge) ? (f!.age as FurAge) : undefined;
  return mask || infantMask || grow !== undefined || age
    ? { ...(mask ? { mask } : {}), ...(infantMask ? { infantMask } : {}), ...(grow !== undefined ? { grow } : {}), ...(age ? { age } : {}) }
    : null;
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
  if (model.fur && (model.fur.mask || model.fur.infantMask || typeof model.fur.grow === 'boolean' || model.fur.age)) block.fur = { ...model.fur };
  if (Object.keys(model.settings).length) block.settings = { ...model.settings };
  return Object.keys(block).length ? block : null;
}

function writeIridescence(i: IridescenceModel): Json | null {
  const out: Json = {};
  if (i.strength) out.strength = writeStrength(i.strength);
  if (i.babyStrength !== null && i.babyStrength !== undefined) out.babyStrength = i.babyStrength;
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
    case 'game':
      return GAME;
    case 'species':
      return { species: r.species };
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
    case 'game':
      return GAME;
    case 'species':
      return { species: m.species };
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
