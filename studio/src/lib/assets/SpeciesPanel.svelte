<script lang="ts">
  import ExportFormatSelect from './ExportFormatSelect.svelte';
  import IkChains from './IkChains.svelte';
  import { formatParam } from './exportFormat.svelte';
  import { onMount } from 'svelte';
  import type { SpeciesRow } from '$lib/rpc/types.gen';
  import AddSkin from '$lib/mods/AddSkin.svelte';
  import OpenInBlender from '$lib/blender/OpenInBlender.svelte';
  import { keyOf } from '$lib/mods/species';
  import type { SpeciesSkinsRow } from '$lib/rpc/types.gen';
  import AllSounds from '$lib/sounds/AllSounds.svelte';
  import SpeciesSounds from '$lib/sounds/SpeciesSounds.svelte';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  const session = getSession();
  const tab = getTab();
  let species = $state<SpeciesRow[]>([]);
  let filter = $state('');
  let lastPack = $state<string | null>(null);
  let addingTo = $state<string | null>(null);
  /** The species whose sounds are open, or ALL for every game sound. */
  const ALL = '__all__';
  let soundsOf = $state<string | null>(null);
  const soundsRow = $derived(species.find((s) => s.key === soundsOf));
  /** The species whose IK chains are open. */
  let ikOf = $state<string | null>(null);
  const ikRow = $derived(species.find((s) => s.key === ikOf));
  const shown = $derived(species.filter((s) => s.displayName.toLowerCase().includes(filter.trim().toLowerCase())));

  /** The dump's species (ids and game skins) by Species tab key, for Open in Blender. */
  let dumped = $state<Map<string, SpeciesSkinsRow>>(new Map());

  onMount(async () => {
    const r = await tab.quietly(() => session.rpc.call('species.list'));
    if (r) species = r.species;
    const d = await tab.quietly(() => session.rpc.call('mods.species'));
    if (d) dumped = new Map(d.species.map((s) => [keyOf(s.speciesId), s]));
  });

  async function exportPack(row: SpeciesRow) {
    const r = await session.runJob('species.pack', { key: row.key, ...formatParam() }, `Species pack: ${row.displayName}`, tab);
    if (!r) return;
    lastPack = r.directory;
    const notes = r.notes?.length ? ` ${r.notes.join(' ')}` : '';
    tab.info(`Exported ${r.models} models and ${r.textures} textures to ${r.directory}.${notes}`);
    if (r.failed) tab.warn(`${r.failed} file(s) of the ${row.displayName} pack could not be exported; see its report in that folder.`);
  }
</script>

<p class="hint">
  A species pack holds one animal's models (.glb, and FBX too when chosen), every texture of its asset group and a targets.json that lists each asset's
  Addressables key — the starting point for a reskin.
</p>
<div class="toolbar">
  <input type="search" placeholder="Find a species…" aria-label="Find a species" bind:value={filter} />
  <ExportFormatSelect />
  {#if lastPack}<button onclick={() => session.platform.reveal(lastPack!)}>Show in Explorer</button>{/if}
  <button onclick={() => (soundsOf = soundsOf === ALL ? null : ALL)}>All sounds…</button>
</div>
<div class="table-wrap">
  <table class="grid">
    <thead><tr><th>Species</th><th>Textures</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
    <tbody>
      {#each shown as row (row.key)}
        <tr>
          <td>{row.displayName}{#if row.vivarium} <span class="badge ok">vivarium</span>{/if}</td>
          <td>{row.textures}</td>
          <td>
            <button disabled={session.busy} aria-label="Export {row.displayName} pack" onclick={() => exportPack(row)}>Export pack</button>
            <button aria-label="Add a skin to {row.displayName}" onclick={() => (addingTo = addingTo === row.key ? null : row.key)}>Add a skin…</button>
            <button aria-label="Sounds of {row.displayName}" onclick={() => (soundsOf = soundsOf === row.key ? null : row.key)}>Sounds…</button>
            <button aria-label="IK chains of {row.displayName}" onclick={() => (ikOf = ikOf === row.key ? null : row.key)}>IK chains…</button>
            {#if dumped.get(row.key)}
              {@const d = dumped.get(row.key)!}
              <OpenInBlender species={d.speciesId} skins={d.skins.map((k) => k.name)} />
            {/if}
          </td>
        </tr>
      {:else}
        <tr><td colspan="3" class="hint">No species match.</td></tr>
      {/each}
    </tbody>
  </table>
</div>
{#if addingTo}{#key addingTo}<AddSkin speciesKey={addingTo} />{/key}{/if}
{#if soundsOf === ALL}
  <AllSounds />
{:else if soundsRow}
  {#key soundsRow.key}<SpeciesSounds speciesKey={soundsRow.key} displayName={soundsRow.displayName} />{/key}
{/if}
{#if ikRow}{#key ikRow.key}<IkChains speciesKey={ikRow.key} displayName={ikRow.displayName} />{/key}{/if}
