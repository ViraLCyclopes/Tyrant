<script lang="ts">
  import { asRpcError } from '$lib/rpc/client';
  import type { ModDetail, ModMaterialInfo, ModMaterialProperty, ModSkinDto } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import IridescencePanel from './IridescencePanel.svelte';
  import { cloneMaterial, parseIridescenceJson, parseMaterial, replaceMaterial, toMaterialsJson, type MaterialModel } from './materials';
  import type { ModDoc } from './modDoc.svelte';

  /**
   * The Material card of a skin or a species: iridescence and the shader settings, over what applies below (species, starting
   * skin, the game). Saves like the colour editor (undo for free); `onDraft` gets what it would save, for a live preview.
   */
  let { doc, skin, species, onDraft }: { doc: ModDoc; skin?: ModSkinDto; species?: string; onDraft?: (materialsJson: string | null) => void } = $props();
  const session = getSession();
  let info = $state.raw<ModMaterialInfo | null>(null);
  let reason = $state<string | null>(null);
  let generation = 0;

  const target = $derived(skin ? { skin: skin.id } : { species: species ?? '' });
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

  const inheritedOf = (p: ModMaterialProperty): unknown => (p.inherited === null ? null : JSON.parse(p.inherited));
  const shownValue = (v: unknown) => (v === null ? 'not set' : Array.isArray(v) ? v.join(', ') : String(v));
  const clamp = (p: ModMaterialProperty, v: number) => Math.min(p.max, Math.max(p.min, v));

  function setNumber(p: ModMaterialProperty, text: string) {
    setSetting(p.key, text === '' ? null : clamp(p, Number(text)));
  }

  function setComponent(p: ModMaterialProperty, n: number, text: string) {
    const own = shown?.settings[p.key];
    const base = Array.isArray(own) ? own : ((inheritedOf(p) as number[] | null) ?? [1, 1]);
    const next = [base[0] ?? 0, base[1] ?? 0];
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
    <IridescencePanel {doc} material={name} model={shown.iridescence} {inherited} gameStrength={info.gameIridescence} gameHasRamp={info.gameHasRamp} {skin} {species}
      canImport={!!skin || saved !== null} onEdit={(change, save) => void edit(change, save)} />

    {#each groups as [group, rows] (group)}
      <fieldset class="group">
        <legend>{group}</legend>
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
                {#each ['x', 'y'] as axis, n (axis)}
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
</style>
