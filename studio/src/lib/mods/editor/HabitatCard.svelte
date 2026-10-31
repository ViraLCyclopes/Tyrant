<script lang="ts">
  import type { ModAnimalFieldDto } from '$lib/rpc/types.gen';
  import { BIOMES, flagList, flagText } from './speciesIdentity';

  /** What fences hold a species and how it feels about water, with plain names, from its full animal record: `values` is the record's edits (path → text), shared with the Full animal record and saved with the species. */
  let { fields, values = $bindable() }: { fields: ModAnimalFieldDto[]; values: Record<string, string> } = $props();

  type Option = readonly [value: string, label: string];
  const FENCE_HEIGHT: Option[] = [['Height1M', '1 m'], ['Height2_5M', '2.5 m'], ['Height4M', '4 m'], ['Height6M', '6 m']];
  const FENCE_STRENGTH: Option[] = ['None', 'Low', 'Medium', 'High', 'Extreme'].map((n) => [n, n]);
  const WATER: Option[] = [
    ['StrongDislike', 'Hates water'], ['MediumDislike', 'Dislikes water'], ['MildDislike', 'Slightly dislikes water'], ['Ambivalent', "Doesn't mind water"],
    ['MildLike', 'Slightly likes water'], ['MediumLike', 'Likes water'], ['StrongLike', 'Loves water'],
  ];

  const heightPath = '/MinFenceHeight';
  const strengthPath = '/MinFenceStrength';
  const waterPath = '/waterAffinity';
  const depthPath = '/minimumWaterDepth';
  const biomesPath = '/treeBiomes';
  const nurseryPath = '/nurseryBackgroundBiome';
  const BIOME_OPTIONS: Option[] = BIOMES.map((b) => [b, b]);

  const byPath = $derived(new Map(fields.filter((f) => f.editable).map((f) => [f.path, f])));
  const has = (path: string) => byPath.has(path);
  const valueAt = (path: string) => values[path] ?? byPath.get(path)?.value ?? '';

  /** The options, plus the record's own value when it isn't one of them, so nothing is silently changed. */
  const biomes = $derived(flagList(valueAt(biomesPath), BIOMES));
  function toggleBiome(biome: string, on: boolean) {
    const rest = biomes.filter((b) => b !== biome);
    values[biomesPath] = flagText(on ? [...rest, biome] : rest, BIOMES);
  }

  const withCurrent = (options: readonly Option[], current: string): readonly Option[] =>
    current === '' || options.some(([v]) => v === current) ? options : [...options, [current, current]];
</script>

{#snippet pick(path: string, label: string, options: readonly Option[])}
  {#if has(path)}
    <label>{label}
      <select value={valueAt(path)} onchange={(e) => (values[path] = e.currentTarget.value)}>
        {#each withCurrent(options, valueAt(path)) as [v, text] (v)}<option value={v}>{text}</option>{/each}
      </select>
    </label>
  {/if}
{/snippet}

<section class="habitat" aria-label="Habitat">
  <h3>Habitat</h3>
  {#if has(biomesPath)}
    <div class="biomes" role="group" aria-label="Biomes">
      <span class="label">Biomes</span>
      <div class="ticks">
        {#each BIOMES as biome (biome)}
          <label class="tick"><input type="checkbox" checked={biomes.includes(biome)} onchange={(e) => toggleBiome(biome, e.currentTarget.checked)} /> {biome}</label>
        {/each}
      </div>
      <span class="hint">The biomes it likes: the pictures under Nature requirements in the game's Exhibit tab.</span>
      {#if biomes.length === 0}
        <p class="warning" role="alert">No biome is ticked: the species shows no biomes, the biome part of its Nature welfare is always 0, and herbivores never browse.</p>
      {/if}
    </div>
  {/if}
  <div class="grid">
    {@render pick(nurseryPath, 'Nursery background', BIOME_OPTIONS)}
    {@render pick(heightPath, 'Fence height', FENCE_HEIGHT)}
    {@render pick(strengthPath, 'Fence strength', FENCE_STRENGTH)}
    {@render pick(waterPath, 'Water liking', WATER)}
    {#if has(depthPath)}
      <label>Minimum water depth (m)
        <input type="number" min="0" step="any" value={valueAt(depthPath)} oninput={(e) => (values[depthPath] = e.currentTarget.value)} />
        <span class="hint">Only the Paleopedia shows this; it doesn't change behaviour.</span>
      </label>
    {/if}
  </div>
  <span class="hint">Plant coverage, water, temperature and bed are in Animal stats. Space per animal is on the Social card.</span>
</section>

<style>
  .habitat { display: grid; gap: 10px; margin-top: 12px; }
  h3 { margin: 0; }
  .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 14px; }
  label { display: grid; gap: 4px; color: var(--muted); }
  select, input[type='number'] { font: inherit; background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: 7px 9px; width: 100%; box-sizing: border-box; }
  .hint { font-size: 12px; }
  .biomes { display: grid; gap: 6px; }
  .label { color: var(--muted); }
  .ticks { display: flex; flex-wrap: wrap; gap: 6px 16px; }
  label.tick { display: flex; align-items: center; gap: 6px; color: var(--text); }
  .warning { color: var(--warn-text); font-size: 13px; margin: 0; }
</style>
