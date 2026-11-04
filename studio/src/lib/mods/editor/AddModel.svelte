<script lang="ts">
  import { onMount } from 'svelte';
  import type { CustomSpeciesRow, ModSpeciesResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import ModelRigHost from './ModelRigHost.svelte';
  import { askFor, chooseModelRig, createRigAsk } from './modelRig';
  import type { ModDoc } from './modDoc.svelte';

  /** The mod editor's Models → + Add: a species' model from your .glb, into this mod (as Replace model in a mod… does). */
  let { doc, onDone }: { doc: Pick<ModDoc, 'id' | 'detail' | 'edit'>; onDone: (target: string | null) => void } = $props();

  const session = getSession();
  const rigAsk = createRigAsk();
  const tab = getTab();
  let data = $state.raw<ModSpeciesResult | null>(null);
  /** This mod's new species: first in the list, as a mod that adds one usually means it. A dump made with the mod installed lists them too. */
  let own = $state.raw<CustomSpeciesRow[]>([]);
  const dumped = $derived((data?.species ?? []).filter((s) => !own.some((o) => o.speciesId === s.speciesId)));
  let species = $state('');
  let file = $state('');
  const ownChosen = $derived(own.some((o) => o.speciesId === species));

  onMount(async () => {
    // Fetched together so the form does not appear before the list that preselects it is in.
    const [result, custom] = await Promise.all([
      tab.quietly(() => session.rpc.call('mods.species')),
      tab.quietly(() => session.rpc.call('mods.customSpecies', { id: doc.id })),
    ]);
    own = custom?.species.filter((s) => s.own) ?? [];
    data = result;
    species = own[0]?.speciesId ?? data?.species.find((s) => !s.vivarium)?.speciesId ?? data?.species[0]?.speciesId ?? '';
  });

  async function browse() {
    const picked = await session.platform.openFile(`Choose your model for ${species || 'the species'} (.glb or .fbx)`, ['glb', 'fbx']);
    if (picked) file = picked;
  }

  // A question asked for one species is not answered for another: choosing another species cancels it.
  $effect(() => {
    void species;
    return () => rigAsk.dispose();
  });

  async function add() {
    const [path, target] = [file.trim(), species]; // the destination the question is about
    const rig = await tab.safely(() => chooseModelRig(session.rpc, { id: doc.id, file: path, target }, { reimport: false, action: 'Add' }, askFor(rigAsk, () => species === target)));
    if (rig && (await doc.edit('mods.replaceModel', { file: path, target, rig }))) onDone(target);
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
          {#each own as s (s.speciesId)}<option value={s.speciesId}>{s.name} (this mod)</option>{/each}
          {#each dumped as s (s.speciesId)}<option value={s.speciesId}>{s.speciesId}{s.vivarium ? ' (vivarium)' : ''}</option>{/each}
        </select>
      </label>
      <label>Your model <input aria-label="Your model" placeholder="a .glb or .fbx" bind:value={file} /></label>
      <button onclick={browse}>Browse…</button>
    </div>
    <div class="row">
      <button class="primary" disabled={!file.trim() || !species || session.busy} onclick={add}>Add</button>
      <button onclick={() => onDone(null)}>Cancel</button>
    </div>
    <ModelRigHost ask={rigAsk} />
    {#if ownChosen}
      <p class="hint">
        This is the species' own model: its rig edit also re-aims the species' head look. Skins with their own model keep it, but share that head look, so give them the same neck.
      </p>
    {/if}
    <p class="hint">
      Start from Tyrant's export of the species' prefab (Assets tab → the prefab → Export selected). In Blender keep the armature, its
      bone names, the growth shape keys and the material names. Tyrant checks the model and builds its levels of detail. To keep a rig edit's scale, export it from Blender with Export model for Tyrant… in the Tyrant panel.
    </p>
  {/if}
</section>

<style>
  .row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin: 8px 0; }
</style>
