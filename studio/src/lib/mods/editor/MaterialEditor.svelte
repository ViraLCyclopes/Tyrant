<script lang="ts">
  import { asRpcError } from '$lib/rpc/client';
  import type { ModDetail, ModMaterialInfo, ModMaterialProperty, ModSkinDto, ModSpeciesPresets } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import IridescencePanel from './IridescencePanel.svelte';
  import { applyPresetBlock, cloneMaterial, GAME, parseFurJson, parseIridescenceJson, parseMaterial, replaceMaterial, START_FUR_LENGTH, toMaterialsJson, type MaterialModel } from './materials';
  import type { ModDoc } from './modDoc.svelte';

  /**
   * The Material card of a skin or a species: iridescence and the shader settings, over what applies below (species, starting
   * skin, the game). Saves like the colour editor (undo for free); `onDraft` gets what it would save, for a live preview.
   * "Start from a species…" fills the iridescence or the fur from a vanilla species (mods.speciesPreset, the in-game editor's
   * preset logic): its ramp and mask by reference, its numbers copied; everything stays editable.
   */
  let { doc, skin, species, vanillaSkin, onDraft }: { doc: ModDoc; skin?: ModSkinDto; species?: string; vanillaSkin?: { species: string; skin: string }; onDraft?: (materialsJson: string | null) => void } = $props();
  const session = getSession();
  let info = $state.raw<ModMaterialInfo | null>(null);
  let reason = $state<string | null>(null);
  let generation = 0;

  const target = $derived(skin ? { skin: skin.id } : vanillaSkin ? { vanillaSkin: `${vanillaSkin.species}/${vanillaSkin.skin}` } : { species: species ?? '' });
  const name = $derived(info?.name ?? null);
  const saved = $derived(doc.detail ? savedIn(doc.detail) : null);
  const model = $derived(name ? parseMaterial(saved, name) : null);
  const inherited = $derived(parseIridescenceJson(info?.inheritedIridescenceJson ?? null));
  /** A change shown before its save comes back (or while a slider moves, never saved). */
  let live = $state.raw<MaterialModel | null>(null);
  const shown = $derived(live ?? model);
  const groups = $derived.by(() => {
    const map = new Map<string, ModMaterialProperty[]>();
    for (const p of info?.properties ?? []) map.set(p.group, [...(map.get(p.group) ?? []), p]);
    return [...map];
  });

  function savedIn(detail: ModDetail): string | null {
    if (skin) return detail.skins.find((s) => s.id === skin.id)?.materialsJson ?? null;
    if (vanillaSkin) {
      const [sp, sk] = [vanillaSkin.species.toLowerCase(), vanillaSkin.skin.toLowerCase()];
      return detail.vanillaSkinMaterials?.find((m) => m.species.toLowerCase() === sp && m.skin.toLowerCase() === sk)?.materialsJson ?? null;
    }
    const wanted = (species ?? '').toLowerCase();
    return detail.speciesMaterials?.find((m) => m.species.toLowerCase() === wanted)?.materialsJson ?? null;
  }

  async function load() {
    const mine = ++generation;
    try {
      const r = await session.rpc.call('mods.materialInfo', { id: doc.id, ...target });
      if (mine !== generation) return;
      info = r;
      reason = r.problem;
    } catch (e) {
      if (mine !== generation) return;
      info = null;
      reason = asRpcError(e).message;
    }
  }

  /** The vanilla species to start from (shimmering ones, furred ones), read once. */
  let presets = $state.raw<ModSpeciesPresets | null>(null);
  $effect(() => {
    void session.rpc.call('mods.speciesPresets').then(
      (r) => (presets = r),
      () => (presets = null), // no lists: the pickers stay empty
    );
  });

  /** Merges the species' preset into this level's block and saves it (Undo takes it back). */
  async function startFrom(kind: 'iridescence' | 'fur', species: string) {
    if (!species) return;
    try {
      const r = await session.rpc.call('mods.speciesPreset', { species, kind });
      void edit((m) => applyPresetBlock(m, r.materialJson), true);
    } catch (e) {
      reason = asRpcError(e).message;
    }
  }

  // Inherited values follow every change to the mod (a species entry, a starting skin).
  $effect(() => {
    void doc.detail?.revision;
    void load();
  });

  // What was saved (or undone) replaces any change still shown.
  $effect(() => {
    void saved;
    live = null;
  });

  $effect(() => {
    if (!onDraft || !name || !shown) return;
    const json = replaceMaterial(saved, name, shown);
    const send = onDraft;
    const timer = setTimeout(() => send(json), 250);
    return () => clearTimeout(timer);
  });

  /** Applies a change to the material as it is when the save's turn comes (quick changes build on each other); save false only shows it. */
  async function edit(change: (m: MaterialModel) => void, save: boolean) {
    const material = name;
    if (!material || !shown) return;
    const next = cloneMaterial(shown);
    change(next);
    live = next;
    if (!save) return;
    const scope = target;
    const ok = await doc.edit('mods.setMaterials', (detail) => {
      const before = savedIn(detail);
      const was = parseMaterial(before, material);
      const after = parseMaterial(before, material);
      change(after);
      return toMaterialsJson(material, after) === toMaterialsJson(material, was) ? null : { ...scope, materials: replaceMaterial(before, material, after) };
    });
    if (!ok) live = null;
  }

  function setSetting(key: string, value: number | string | number[] | null) {
    void edit((m) => {
      if (value === null) delete m.settings[key];
      else m.settings[key] = value;
    }, true);
  }

  /** A species' fur masks need its material settings saved first. */
  const canImportFur = $derived(!!skin || saved !== null);

  const inheritedOf = (p: ModMaterialProperty): unknown => (p.inherited === null ? null : JSON.parse(p.inherited));
  const shownValue = (v: unknown) => (v === null ? 'not set' : Array.isArray(v) ? v.join(', ') : String(v));
  const clamp = (p: ModMaterialProperty, v: number) => Math.min(p.max, Math.max(p.min, v));

  function setNumber(p: ModMaterialProperty, text: string) {
    setSetting(p.key, text === '' ? null : clamp(p, Number(text)));
  }

  /** Image imports (iridescence, fur masks) need a species' material settings saved first; skins and vanilla skins do not. */
  const canImportFile = $derived(!!skin || !!vanillaSkin || saved !== null);
  /** How many numbers a vector setting has (x, y, z, w); an older server says nothing: two. */
  const AXES = ['x', 'y', 'z', 'w'];
  const countOf = (p: ModMaterialProperty) => Math.min(4, Math.max(2, p.components || 2));

  async function importFur(part: 'furMask' | 'furInfantMask') {
    const png = await session.platform.openFile(`Choose the ${part === 'furMask' ? 'fur' : 'baby fur'} mask PNG`, ['png']);
    if (png) await doc.edit('mods.setMaterialFile', { ...target, material: name, part, png });
  }

  function removeFur(part: 'furMask' | 'furInfantMask') {
    return doc.edit('mods.setMaterialFile', { ...target, material: name, part, png: null });
  }

  /** The fur block under this level (masks, grow), from mods.materialInfo. */
  const inheritedFur = $derived(parseFurJson(info?.inheritedFurJson));
  /** Grown here, else by a level below. */
  const growing = $derived((shown?.fur?.grow ?? inheritedFur?.grow) === true);
  /** True while the fur length is the one ticking Grow fur set: unticking takes it back. */
  let seededLength = false;
  // The card can move to another skin, species or vanilla skin without being made again: a length seeded there is not this one's.
  // A string, so a new skin object for the same skin (after every save) changes nothing.
  const targetKey = $derived(JSON.stringify(target));
  $effect(() => {
    void targetKey;
    seededLength = false;
  });

  /** A fur block with one change, or none when nothing is left in it. */
  function setFur(m: MaterialModel, change: (fur: NonNullable<MaterialModel['fur']>) => void) {
    const fur = { ...(m.fur ?? {}) };
    change(fur);
    if (fur.mask || fur.infantMask || fur.grow !== undefined) m.fur = fur;
    else delete m.fur;
  }

  /**
   * Grow fur (fur.grow): on also sets a starting fur length when neither this level nor anything below gives the fur one, so it
   * shows at once. Off writes false over a level below that grows fur, else removes the key; it also takes back the starting
   * length while it is still the one on set.
   */
  function setGrow(on: boolean) {
    const below = info?.properties.find((p) => p.key === '_Length');
    const inheritedLength = below ? inheritedOf(below) : null;
    const own = shown?.settings._Length;
    const length = typeof own === 'number' ? own : typeof inheritedLength === 'number' ? inheritedLength : 0;
    const seed = on && length <= 0;
    const unseed = !on && seededLength && own === START_FUR_LENGTH;
    const overBelow = inheritedFur?.grow === true;
    void edit((m) => {
      setFur(m, (fur) => {
        if (on) fur.grow = true;
        else if (overBelow) fur.grow = false;
        else delete fur.grow;
      });
      if (seed) m.settings._Length = START_FUR_LENGTH;
      if (unseed) delete m.settings._Length;
    }, true);
    seededLength = seed;
  }

  /** The fur mask's Game's over a level below that sets one: "game" (leaving it out would show that one again). */
  function gameFur(part: 'mask' | 'infantMask') {
    void edit((m) => setFur(m, (fur) => (fur[part] = GAME)), true);
  }

  /** What a fur mask row shows: this level's file or the game's, else the one below, else the skin's own. */
  function furShown(own: string | undefined, below: string | undefined): string {
    if (own === GAME) return 'the game’s';
    if (own) return own;
    if (below === GAME) return 'the game’s (from below)';
    return below ? `${below} (from below)` : "the skin's own";
  }

  function setComponent(p: ModMaterialProperty, n: number, text: string) {
    const count = countOf(p);
    const own = shown?.settings[p.key];
    const base = Array.isArray(own) ? own : ((inheritedOf(p) as number[] | null) ?? Array.from({ length: count }, () => 1));
    const next = Array.from({ length: count }, (_, i) => base[i] ?? 0);
    next[n] = text === '' ? 0 : clamp(p, Number(text));
    setSetting(p.key, next);
  }
</script>

<div class="material">
  {#if name}<p class="hint">Material: {name}</p>{/if}
  {#if reason}<p class="hint">{reason}</p>{/if}
  {#if !info}
    {#if !reason}<p class="hint">Loading the material…</p>{/if}
  {:else if shown && name}
    <IridescencePanel {doc} material={name} model={shown.iridescence} {inherited} gameStrength={info.gameIridescence} gameHasRamp={info.gameHasRamp} {skin} {species} {vanillaSkin}
      canImport={canImportFile} presets={presets?.iridescence ?? []} onPreset={(id) => void startFrom('iridescence', id)}
      onEdit={(change, save) => void edit(change, save)} />

    {#each groups as [group, rows] (group)}
      <fieldset class="group">
        <legend>{group}</legend>
        {#if group === 'Fur'}
          <select class="preset" aria-label="Start the fur from a species" value="" disabled={!presets?.fur.length}
            onchange={(e) => { const id = e.currentTarget.value; e.currentTarget.value = ''; void startFrom('fur', id); }}>
            <option value="">Start from a species…</option>
            {#each presets?.fur ?? [] as p (p.species)}<option value={p.species}>{p.name}</option>{/each}
          </select>
          <label class="grow">
            <input type="checkbox" checked={growing} onchange={(e) => setGrow(e.currentTarget.checked)} />
            Grow fur on skins without fur
          </label>
          {#if shown.fur?.grow === undefined && inheritedFur?.grow === true}<p class="hint">Fur is grown by a level below; untick to turn it off here.</p>{/if}
          <p class="hint">Needs tessellated fur in the game's graphics settings. The preview here shows no fur; tune the look in game.</p>
        {/if}
        {#each rows as p (p.key)}
          {@const own = shown.settings[p.key]}
          {@const below = inheritedOf(p)}
          <div class="setting" role="group" aria-label={p.label}>
            <span class="label">{p.label}</span>
            <span class="inherited">{shownValue(below)}{#if below !== null}<span class="from"> from {p.source}</span>{/if}</span>
            <span class="field">
              {#if p.kind === 'color'}
                <input type="color" aria-label={p.label} value={typeof own === 'string' ? own : typeof below === 'string' ? below : '#808080'}
                  onchange={(e) => setSetting(p.key, e.currentTarget.value.toLowerCase())} />
              {:else if p.kind === 'vector'}
                {#each AXES.slice(0, countOf(p)) as axis, n (axis)}
                  <input type="number" aria-label="{p.label} {axis}" min={p.min} max={p.max} step="0.1" value={Array.isArray(own) ? own[n] : ''}
                    placeholder={Array.isArray(below) ? String(below[n]) : ''} onchange={(e) => setComponent(p, n, e.currentTarget.value)} />
                {/each}
              {:else}
                <input type="number" aria-label={p.label} min={p.min} max={p.max} step={p.max - p.min <= 2 ? 0.01 : 0.1} value={typeof own === 'number' ? own : ''}
                  placeholder={typeof below === 'number' ? String(below) : ''} onchange={(e) => setNumber(p, e.currentTarget.value)} />
              {/if}
            </span>
            <button class="ghost x" aria-label="Reset {p.label}" title="Use the value below" disabled={own === undefined} onclick={() => setSetting(p.key, null)}>✕</button>
          </div>
        {/each}
        {#if group === 'Fur'}
          {#each [
            { part: 'furMask' as const, key: 'mask' as const, label: 'Fur mask', file: shown.fur?.mask, below: inheritedFur?.mask },
            { part: 'furInfantMask' as const, key: 'infantMask' as const, label: 'Baby fur mask', file: shown.fur?.infantMask, below: inheritedFur?.infantMask },
          ] as row (row.part)}
            <div class="setting" role="group" aria-label={row.label}>
              <span class="label">{row.label}</span>
              <span class="inherited">{furShown(row.file, row.below)}</span>
              <span class="field">
                <button aria-label="Import {row.label.toLowerCase()}" disabled={!canImportFile} onclick={() => void importFur(row.part)}>Import…</button>
                {#if row.below && row.below !== GAME && row.file !== GAME}
                  <button aria-label="Game's {row.label.toLowerCase()}" title="The game's own, over the one below" onclick={() => gameFur(row.key)}>Game's</button>
                {/if}
              </span>
              <button class="ghost x" aria-label="Remove {row.label.toLowerCase()}" title="Use the one below" disabled={!row.file}
                onclick={() => (row.file === GAME ? void edit((m) => setFur(m, (fur) => delete fur[row.key]), true) : void removeFur(row.part))}>✕</button>
            </div>
          {/each}
        {/if}
      </fieldset>
    {/each}
  {/if}
</div>

<style>
  .material { display: grid; gap: 10px; }
  .material p { margin: 0; }
  .group { border: 1px solid var(--border); border-radius: 6px; padding: 8px 10px; display: grid; gap: 6px; }
  .setting { display: grid; grid-template-columns: 170px 150px 1fr auto; align-items: center; gap: 8px; }
  .label { color: var(--muted); }
  .inherited { font-size: 12px; }
  .from { color: var(--muted); }
  .field { display: flex; gap: 4px; }
  .field input[type='number'] { width: 80px; }
  input[type='color'] { width: 28px; height: 24px; padding: 0; border: 1px solid var(--border); border-radius: 4px; background: none; }
  .x { padding: 0 4px; font-size: 10px; }
  .grow { display: flex; align-items: center; gap: 6px; }
  .preset { justify-self: start; }
</style>
