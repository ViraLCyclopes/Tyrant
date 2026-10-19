<script lang="ts">
  import type { ModSkinDto } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import ColourStops from './ColourStops.svelte';
  import {
    CHANNELS,
    DEFAULT_INDEX,
    DEFAULT_SHAPING,
    DEFAULT_THICKNESS,
    DEFAULT_VARIATION,
    emptyIridescence,
    MASK_SLOTS,
    MAX_RAMP_STOPS,
    maxStrength,
    MUTATIONS,
    type IridescenceModel,
    type MaterialModel,
    type StrengthModel,
  } from './materials';
  import type { ModDoc } from './modDoc.svelte';

  /**
   * The Material card's iridescence: on/off, strength, variation, ramp, mask and shaping. `model` is this skin's or species' own
   * block, `inherited` what applies below it (species or starting skin). `gameStrength` is the vanilla skin's own shimmer in the
   * game (0: none; null: cannot tell, as on a species page, where only some skins shimmer); `gameHasRamp` says the game material
   * has its own ramp and shaping. `onEdit` saves a change, or only previews it (save false).
   */
  let {
    doc,
    material,
    model,
    inherited,
    gameStrength,
    gameHasRamp,
    skin,
    species,
    canImport,
    onEdit,
  }: {
    doc: ModDoc;
    material: string;
    model: IridescenceModel | null;
    inherited: IridescenceModel | null;
    gameStrength: number | null;
    gameHasRamp: boolean;
    skin?: ModSkinDto;
    species?: string;
    canImport: boolean;
    onEdit: (change: (m: MaterialModel) => void, save: boolean) => void;
  } = $props();
  const session = getSession();
  const DEFAULT_STRENGTH = 0.25;
  const NAMES = { albino: 'Albino', melanistic: 'Melanistic', leucistic: 'Leucistic' } as const;
  const SHAPING = [
    { key: 'scale', label: 'Edge falloff' },
    { key: 'power', label: 'Falloff curve' },
    { key: 'bias', label: 'Brightness offset' },
  ] as const;

  /** The game's own shimmer for this skin; unknown counts as none (a neutral "Game's" state). */
  const gameOn = $derived((gameStrength ?? 0) > 0);
  const baselineOn = $derived(inherited?.strength ? maxStrength(inherited.strength) > 0 : gameOn);
  const strength = $derived<StrengthModel | null>(model?.strength ?? inherited?.strength ?? null);
  const on = $derived(strength ? maxStrength(strength) > 0 : gameOn);
  /** What a strength edit starts from when no layer sets one: the game's own, else a visible default. */
  const startStrength = $derived(gameOn ? gameStrength! : DEFAULT_STRENGTH);
  const variation = $derived(model?.variation ?? inherited?.variation ?? DEFAULT_VARIATION);
  const ramp = $derived(model && model.ramp.kind !== 'game' ? model.ramp : (inherited?.ramp ?? model?.ramp ?? { kind: 'game' as const }));
  const mask = $derived(model && model.mask.kind !== 'game' ? model.mask : (inherited?.mask ?? model?.mask ?? { kind: 'game' as const }));
  const shaping = $derived({
    scale: model?.scale ?? inherited?.scale ?? null,
    power: model?.power ?? inherited?.power ?? null,
    bias: model?.bias ?? inherited?.bias ?? null,
  });
  const target = $derived(skin ? { skin: skin.id } : { species: species! });

  /** Changes this level's own block (made from nothing when there is none yet). */
  function edit(change: (i: IridescenceModel) => void, save = true) {
    onEdit((m) => change((m.iridescence ??= emptyIridescence())), save);
  }

  function toggle(turnOn: boolean) {
    onEdit((m) => {
      if (!turnOn) {
        // Over iridescence from below, off is strength 0, keeping this level's ramp, mask and shaping for turning it on again.
        if (baselineOn) (m.iridescence ??= emptyIridescence()).strength = { normal: 0 };
        else m.iridescence = null;
        return;
      }
      const i = (m.iridescence ??= emptyIridescence());
      i.strength = baselineOn ? null : { normal: DEFAULT_STRENGTH };
      const shaped = [inherited?.scale, inherited?.power, inherited?.bias, i.scale, i.power, i.bias].some((v) => v != null);
      if (!gameHasRamp && !shaped) Object.assign(i, DEFAULT_SHAPING);
    }, true);
  }

  function setStrength(variant: 'normal' | (typeof MUTATIONS)[number], value: number | undefined, save: boolean) {
    edit((i) => {
      const next: StrengthModel = { ...(i.strength ?? strength ?? { normal: startStrength }) };
      if (variant === 'normal') next.normal = value ?? 0;
      else if (value === undefined) delete next[variant];
      else next[variant] = value;
      i.strength = next;
    }, save);
  }

  async function importPart(part: 'ramp' | 'mask') {
    const png = await session.platform.openFile(`Choose the iridescence ${part} PNG`, ['png']);
    if (png) await doc.edit('mods.setMaterialFile', { ...target, material, part, png });
  }

  function pickRamp(id: string) {
    if (id === 'file') void importPart('ramp');
    else if (id === 'game') edit((i) => (i.ramp = { kind: 'game' }));
    else if (ramp.kind !== 'stops' && ramp.kind !== 'thinFilm') edit((i) => (i.ramp = { kind: 'stops', stops: ['#3060ff', '#ff60c0'] }));
  }

  function pickMask(id: string) {
    if (id === 'file') void importPart('mask');
    else if (id === 'game') edit((i) => (i.mask = { kind: 'game' }));
    else if (id === 'everywhere') edit((i) => (i.mask = { kind: 'everywhere' }));
    else if (mask.kind !== 'from') edit((i) => (i.mask = { kind: 'from', slot: 'extra', channel: 'r', invert: false }));
  }

  function setThickness(thickness: number, save: boolean) {
    edit((i) => (i.ramp = { kind: 'thinFilm', thickness, index: i.ramp.kind === 'thinFilm' ? i.ramp.index : DEFAULT_INDEX }), save);
  }

  // The thin-film ramp's colours, from the core (the game, the preview and the app share one generator).
  let filmStops = $state<string[]>([]);
  let filmGeneration = 0;
  $effect(() => {
    if (ramp.kind !== 'thinFilm') return;
    const params = { thickness: ramp.thickness, index: ramp.index, count: MAX_RAMP_STOPS };
    const mine = ++filmGeneration;
    const timer = setTimeout(async () => {
      try {
        const r = await session.rpc.call('mods.thinFilm', params);
        if (mine === filmGeneration) filmStops = r.stops.map((c) => c.toLowerCase());
      } catch {
        if (mine === filmGeneration) filmStops = [];
      }
    }, 60);
    return () => clearTimeout(timer);
  });
  const stops = $derived(ramp.kind === 'stops' ? ramp.stops : ramp.kind === 'thinFilm' ? filmStops : null);

  /** Sexes whose own PNG for the mask's slot is missing while the base skin has one: "Edit a copy" copies it in. */
  const copyable = $derived.by(() => {
    if (!skin || mask.kind !== 'from') return [];
    const slot = mask.slot;
    return (['male', 'female'] as const).filter((sex) => !(sex === 'male' ? skin.male : skin.female)?.[slot] && (sex === 'male' ? skin.baseMaleSlots : skin.baseFemaleSlots)?.includes(slot));
  });

  async function copyBase(slot: string) {
    for (const sex of copyable) if (!(await doc.edit('mods.copyBaseFile', { skin: skin!.id, sex, slot }))) return;
  }

  const shape = $derived({
    scale: shaping.scale ?? DEFAULT_SHAPING.scale,
    power: shaping.power ?? DEFAULT_SHAPING.power,
    bias: shaping.bias ?? DEFAULT_SHAPING.bias,
  });
  /** saturate(1 − (scale·(1−x)^power + bias)) for x = N·V from 0 (edge-on) to 1 (facing). */
  const curve = $derived(
    Array.from({ length: 33 }, (_, n) => {
      const x = n / 32;
      const y = 1 - (shape.scale * Math.pow(1 - x, shape.power) + shape.bias);
      const v = Number.isNaN(y) ? 0 : Math.min(1, Math.max(0, y));
      return `${(x * 120).toFixed(1)},${(38 - v * 36).toFixed(1)}`;
    }).join(' '),
  );

  const num = (e: Event & { currentTarget: HTMLInputElement }) => Number(e.currentTarget.value);
  const pct = (v: number) => `${Math.round(v * 100)} %`;
</script>

{#snippet choices(label: string, options: { id: string; label: string; disabled?: boolean }[], current: string, pick: (id: string) => void)}
  <div class="choices" role="radiogroup" aria-label={label}>
    {#each options as o (o.id)}
      <button role="radio" aria-checked={current === o.id} class:on={current === o.id} disabled={o.disabled} onclick={() => pick(o.id)}>{o.label}</button>
    {/each}
  </div>
{/snippet}

<fieldset class="irid">
  <legend><label><input type="checkbox" checked={on} onchange={(e) => toggle(e.currentTarget.checked)} /> Iridescence</label></legend>
  {#if !on}
    <p class="hint">{baselineOn ? 'Turned off here.' : gameStrength === null ? 'Game’s (only some skins shimmer).' : skin ? 'This skin has none in the game.' : 'This material has none.'}</p>
  {:else}
    {#if !model}<p class="hint">Uses {inherited ? 'the settings below this one' : 'the game’s'}; a change here overrides them.</p>{/if}
    <div class="slider">
      <span class="label">Strength</span>
      <input type="range" aria-label="Strength" min="0" max="1" step="0.01" value={strength?.normal ?? startStrength}
        oninput={(e) => setStrength('normal', num(e), false)} onchange={(e) => setStrength('normal', num(e), true)} />
      <span>{strength ? pct(strength.normal) : gameOn ? `game’s, ${pct(gameStrength!)}` : 'game’s'}</span>
    </div>
    <details>
      <summary>Per mutation</summary>
      {#each MUTATIONS as m (m)}
        <div class="slider">
          <span class="label">{NAMES[m]}</span>
          <input type="range" aria-label="{NAMES[m]} strength" min="0" max="1" step="0.01" value={strength?.[m] ?? strength?.normal ?? startStrength}
            oninput={(e) => setStrength(m, num(e), false)} onchange={(e) => setStrength(m, num(e), true)} />
          <span>{strength?.[m] !== undefined ? pct(strength[m]) : 'as normal'}</span>
          <button class="ghost x" aria-label="{NAMES[m]} as normal" disabled={model?.strength?.[m] === undefined} onclick={() => setStrength(m, undefined, true)}>✕</button>
        </div>
      {/each}
    </details>
    <div class="slider">
      <span class="label">Varies between animals</span>
      <input type="range" aria-label="Varies between animals" min="0" max="1" step="0.01" value={variation}
        oninput={(e) => edit((i) => (i.variation = num(e)), false)} onchange={(e) => edit((i) => (i.variation = num(e)))} />
      <span>±{pct(variation)}</span>
    </div>

    <div class="part">
      <span class="label">Colours</span>
      {@render choices('Ramp source', [
        { id: 'game', label: 'Game’s' },
        { id: 'file', label: 'Import image', disabled: !canImport },
        { id: 'gradient', label: 'Gradient' },
      ], ramp.kind === 'stops' || ramp.kind === 'thinFilm' ? 'gradient' : ramp.kind, pickRamp)}
      <button onclick={() => setThickness(DEFAULT_THICKNESS, true)}>Real iridescence (thin film)</button>
      {#if ramp.kind === 'file'}<span class="hint">{ramp.file}</span>{/if}
    </div>
    {#if stops}
      {#if stops.length}
        <ColourStops label="Ramp" value={stops} max={MAX_RAMP_STOPS} baseLabel={null} onChange={(v) => v && edit((i) => (i.ramp = { kind: 'stops', stops: v }))} />
      {:else}<p class="hint">Working out the colours…</p>{/if}
      <div class="slider">
        <span class="label">Film thickness</span>
        <input type="range" aria-label="Film thickness" min="100" max="1000" step="10" value={ramp.kind === 'thinFilm' ? ramp.thickness : DEFAULT_THICKNESS}
          oninput={(e) => setThickness(num(e), false)} onchange={(e) => setThickness(num(e), true)} />
        <span>{ramp.kind === 'thinFilm' ? `${ramp.thickness} nm` : ''}</span>
      </div>
    {/if}

    <div class="part">
      <span class="label">Where</span>
      {@render choices('Mask', [
        { id: 'everywhere', label: 'Everywhere' },
        { id: 'game', label: 'Game’s' },
        { id: 'file', label: 'Import image', disabled: !canImport },
        ...(skin ? [{ id: 'from', label: 'From a map channel' }] : []),
      ], mask.kind, pickMask)}
      {#if mask.kind === 'file'}<span class="hint">{mask.file}</span>{/if}
    </div>
    {#if mask.kind === 'from'}
      {@const from = mask}
      <div class="row">
        <select aria-label="Mask map" value={from.slot} onchange={(e) => edit((i) => (i.mask = { ...from, slot: e.currentTarget.value as typeof from.slot }))}>
          {#each MASK_SLOTS as s (s)}<option value={s}>{s}</option>{/each}
        </select>
        <select aria-label="Mask channel" value={from.channel} onchange={(e) => edit((i) => (i.mask = { ...from, channel: e.currentTarget.value as typeof from.channel }))}>
          {#each CHANNELS as c (c)}<option value={c}>{c.toUpperCase()}</option>{/each}
        </select>
        <label><input type="checkbox" checked={from.invert} onchange={(e) => edit((i) => (i.mask = { ...from, invert: e.currentTarget.checked }))} /> Invert</label>
        {#if copyable.length}
          <span class="hint">This skin uses the base skin's {from.slot} map; the mask needs its own copy.</span>
          <button onclick={() => copyBase(from.slot)}>Edit a copy</button>
        {/if}
      </div>
    {/if}
    {#if !canImport}<p class="hint">Import image works once the species has a material setting saved.</p>{/if}

    <div class="shaping">
      {#each SHAPING as f (f.key)}
        <label>{f.label}
          <input type="number" aria-label={f.label} min="-10" max="10" step="0.05" value={model?.[f.key] ?? ''} placeholder={shaping[f.key]?.toString() ?? 'game’s'}
            onchange={(e) => { const v = e.currentTarget.value; edit((i) => (i[f.key] = v === '' ? null : Math.min(10, Math.max(-10, Number(v))))); }} />
        </label>
      {/each}
      <svg viewBox="0 0 120 40" role="img" aria-label="Strength across viewing angles, edge-on to facing"><polyline points={curve} /></svg>
    </div>
  {/if}
</fieldset>

<style>
  .irid { border: 1px solid var(--border); border-radius: 6px; padding: 8px 10px; display: grid; gap: 8px; }
  .irid p { margin: 0; }
  .slider, .part, .row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .label { color: var(--muted); min-width: 90px; }
  details { display: grid; gap: 6px; }
  summary { cursor: pointer; color: var(--muted); }
  .x { padding: 0 4px; font-size: 10px; }
  .choices { display: inline-flex; border: 1px solid var(--border); border-radius: 6px; overflow: hidden; }
  .choices button { border: none; border-radius: 0; padding: 4px 10px; background: none; color: var(--muted); }
  .choices button.on { background: var(--accent); color: var(--accent-text); }
  .shaping { display: flex; align-items: end; gap: 10px; flex-wrap: wrap; }
  .shaping label { display: grid; gap: 2px; color: var(--muted); font-size: 12px; }
  .shaping input { width: 80px; }
  svg { width: 120px; height: 40px; background: var(--panel-2); border-radius: 4px; }
  polyline { fill: none; stroke: var(--accent); stroke-width: 1.5; }
</style>
