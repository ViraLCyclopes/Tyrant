<script lang="ts">
  import { onMount } from 'svelte';
  import type { ModSpeciesResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /** The mod editor's Models → + Add: a species' model from your .glb, into this mod (as Replace model in a mod… does). */
  let { doc, onDone }: { doc: Pick<ModDoc, 'id' | 'detail' | 'edit'>; onDone: (target: string | null) => void } = $props();

  const session = getSession();
  const tab = getTab();
  let data = $state.raw<ModSpeciesResult | null>(null);
  let species = $state('');
  let file = $state('');

  onMount(async () => {
    data = await tab.quietly(() => session.rpc.call('mods.species'));
    species = data?.species.find((s) => !s.vivarium)?.speciesId ?? data?.species[0]?.speciesId ?? '';
  });

  async function browse() {
    const picked = await session.platform.openFile(`Choose your model for ${species || 'the species'} (.glb or .fbx)`, ['glb', 'fbx']);
    if (picked) file = picked;
  }

  async function add() {
    if (await doc.edit('mods.replaceModel', { file: file.trim(), target: species })) onDone(species);
  }
</script>

<section class="card">
  <h2>Add a model</h2>
  {#if !data}
    <p class="hint">Loading the species…</p>
  {:else if !data.hasDump}
    <p class="hint">Models need the game's data (species): on the Workspace tab click Run data dump, then try again.</p>
    <div class="row"><button onclick={() => onDone(null)}>Cancel</button></div>
  {:else}
    <div class="row">
      <label>
        Species
        <select aria-label="Species" bind:value={species}>
          {#each data.species as s (s.speciesId)}<option value={s.speciesId}>{s.speciesId}{s.vivarium ? ' (vivarium)' : ''}</option>{/each}
        </select>
      </label>
      <label>Your model <input aria-label="Your model" placeholder="a .glb or .fbx" bind:value={file} /></label>
      <button onclick={browse}>Browse…</button>
    </div>
    <div class="row">
      <button class="primary" disabled={!file.trim() || !species || session.busy} onclick={add}>Add</button>
      <button onclick={() => onDone(null)}>Cancel</button>
    </div>
    <p class="hint">
      Start from Tyrant's export of the species' prefab (Assets tab → the prefab → Export selected). In Blender keep the armature, its
      bone names, the growth shape keys and the material names. Tyrant checks the model and builds its levels of detail.
    </p>
  {/if}
</section>

<style>
  .row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin: 8px 0; }
</style>
