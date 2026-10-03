<script lang="ts">
  import { onMount } from 'svelte';
  import type { SpeciesRow } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';

  const session = getSession();
  let species = $state<SpeciesRow[]>([]);
  let filter = $state('');
  let lastPack = $state<string | null>(null);
  const shown = $derived(species.filter((s) => s.displayName.toLowerCase().includes(filter.trim().toLowerCase())));

  onMount(async () => {
    const r = await session.quietly(() => session.rpc.call('species.list'));
    if (r) species = r.species;
  });

  async function exportPack(row: SpeciesRow) {
    const r = await session.runJob('species.pack', { key: row.key }, `Species pack: ${row.displayName}`);
    if (!r) return;
    lastPack = r.directory;
    const failed = r.failed ? ` (${r.failed} failed)` : '';
    session.notice = `Exported ${r.models} models and ${r.textures} textures to ${r.directory}${failed}.`;
  }
</script>

<p class="hint">
  A species pack holds one animal's models (.glb), every texture of its asset group and a targets.json that lists each asset's
  Addressables key — the starting point for a reskin.
</p>
<div class="toolbar">
  <input type="search" placeholder="Find a species…" aria-label="Find a species" bind:value={filter} />
  {#if lastPack}<button onclick={() => session.platform.reveal(lastPack!)}>Show in Explorer</button>{/if}
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
          </td>
        </tr>
      {:else}
        <tr><td colspan="3" class="hint">No species match.</td></tr>
      {/each}
    </tbody>
  </table>
</div>
