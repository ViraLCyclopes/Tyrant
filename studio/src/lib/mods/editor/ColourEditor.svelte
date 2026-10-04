<script lang="ts">
  import type { ModSkinDto } from '$lib/rpc/types.gen';
  import {
    emptySet,
    hasColour,
    parseColors,
    toColorsJson,
    VARIANTS,
    type ColorsModel,
    type Range,
    type SetModel,
    type TintModel,
    type Variant,
  } from './colors';
  import ColourPreviewStrip from './ColourPreviewStrip.svelte';
  import ColourStops from './ColourStops.svelte';
  import SkinModel3D from './SkinModel3D.svelte';
  import type { ModDoc } from './modDoc.svelte';
  import RangeField from './RangeField.svelte';

  let { doc, skin }: { doc: ModDoc; skin: ModSkinDto } = $props();
  let variant = $state<Variant>('normal');
  const model = $derived(parseColors(skin.colorsJson));
  const meta = $derived(VARIANTS.find((v) => v.id === variant)!);
  const set = $derived<SetModel | null>(model[meta.key]);
  const tint = $derived<TintModel | null>(variant === 'normal' ? model.tint : (set?.tint ?? null));
  const needsColour = $derived(variant === 'normal' && !hasColour(set));
  const exact = $derived(!!tint && [tint.hue, tint.saturation, tint.value].every((r) => r !== null && r.min === 0 && r.max === 0));
  const ZERO: Range = { min: 0, max: 0 };
  const NO_TINT: TintModel = { hue: null, saturation: null, value: null };

  /**
   * Applies one change to the skin's colours as they are when the save's turn comes (so quick changes build on each
   * other), and saves the result; nothing is sent when the colours stay the same.
   */
  async function save(change: (m: ColorsModel) => void) {
    const id = skin.id;
    await doc.edit('mods.setColors', (detail) => {
      const saved = detail.skins.find((s) => s.id === id)?.colorsJson ?? null;
      const next = parseColors(saved);
      change(next);
      const json = toColorsJson(next);
      return json === saved || (json !== null && saved !== null && JSON.stringify(JSON.parse(json)) === JSON.stringify(JSON.parse(saved))) ? null : { skin: id, colors: json };
    });
  }

  function setField<K extends keyof SetModel>(field: K, value: SetModel[K]) {
    const key = meta.key;
    void save((m) => {
      const target = m[key] ?? emptySet();
      target[field] = value;
      m[key] = target;
    });
  }

  function setTintModel(m: ColorsModel, value: TintModel | null) {
    if (variant === 'normal') m.tint = value;
    else {
      const target = m[meta.key] ?? emptySet();
      target.tint = value;
      m[meta.key] = target;
    }
  }

  function setTint(field: keyof TintModel, value: Range | null) {
    void save((m) => setTintModel(m, { ...(tint ?? NO_TINT), [field]: value }));
  }

  function setExact(on: boolean) {
    void save((m) => setTintModel(m, on ? { hue: ZERO, saturation: ZERO, value: ZERO } : null));
  }
</script>

<div class="colours">
  <SkinModel3D {doc} {skin} colorsJson={skin.colorsJson} {variant} />
  <ColourPreviewStrip {doc} {skin} colorsJson={skin.colorsJson} {variant} />

  <div class="variants" role="radiogroup" aria-label="Animal type">
    {#each VARIANTS as v (v.id)}
      <label class:on={variant === v.id}><input type="radio" name="variant-{skin.id}" value={v.id} bind:group={variant} />{v.label}</label>
    {/each}
  </div>
  <p class="hint">
    {variant === 'normal' ? 'Colours of normal animals of this skin (vanilla only colours mutations).' : `Colours of ${meta.label.toLowerCase()} animals of this skin.`}
    Each animal gets a random point on a gradient and in a range.
  </p>

  <ColourStops label="Colour A" value={set?.a ?? null} onChange={(v) => setField('a', v)} />
  <ColourStops label="Colour B" value={set?.b ?? null} onChange={(v) => setField('b', v)} />
  {#if needsColour}<p class="hint">Set colour A or B first: the game needs a pattern colour before strength, softness, secondary and eyes apply.</p>{/if}
  <ColourStops label="Secondary" value={set?.secondary ?? null} disabled={needsColour} onChange={(v) => setField('secondary', v)} />
  <ColourStops label="Eyes" value={set?.eye ?? null} disabled={needsColour} onChange={(v) => setField('eye', v)} />
  <RangeField label="Strength" value={set?.strength ?? null} min={0} max={1} fallback={{ min: 0.6, max: 0.8 }} disabled={needsColour} onChange={(v) => setField('strength', v)} />
  <RangeField label="Softness" value={set?.softness ?? null} min={0.01} max={1} fallback={{ min: 0.1, max: 0.35 }} disabled={needsColour} onChange={(v) => setField('softness', v)} />

  <fieldset class="tint">
    <legend>Tint</legend>
    <label><input type="checkbox" checked={exact} onchange={(e) => setExact(e.currentTarget.checked)} /> Exact colours</label>
    {#if !exact}
      <RangeField label="Hue" value={tint?.hue ?? null} min={-1} max={1} fallback={{ min: -0.05, max: 0.05 }} onChange={(v) => setTint('hue', v)} />
      <RangeField label="Saturation" value={tint?.saturation ?? null} min={-1} max={1} fallback={ZERO} onChange={(v) => setTint('saturation', v)} />
      <RangeField label="Value" value={tint?.value ?? null} min={-1} max={1} fallback={ZERO} onChange={(v) => setTint('value', v)} />
    {/if}
  </fieldset>
</div>

<style>
  .colours { display: grid; gap: 10px; }
  .colours p { margin: 0; }
  .variants { display: inline-flex; border: 1px solid var(--border); border-radius: 6px; overflow: hidden; width: fit-content; }
  .variants label { position: relative; padding: 4px 12px; color: var(--muted); cursor: pointer; }
  .variants label.on { background: var(--accent); color: var(--accent-text); }
  .variants input { position: absolute; opacity: 0; pointer-events: none; }
  .tint { border: 1px solid var(--border); border-radius: 6px; padding: 8px 10px; display: grid; gap: 8px; }
</style>
