<script lang="ts">
  import type { AssetRow, ModsListResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  /** Replace model in a mod… on a prefab: the user's .glb from Blender becomes the species' model in a mod. */
  let { asset }: { asset: AssetRow } = $props();

  const NEW = '__new__';
  const session = getSession();
  const tab = getTab();
  let open = $state(false);
  let mods = $state<ModsListResult | null>(null);
  let target = $state(NEW);
  let newId = $state('my-mod');
  let file = $state('');
  const workspaceMods = $derived((mods?.mods ?? []).filter((m) => m.state !== 'gameOnly'));

  async function start() {
    open = true;
    mods = await tab.quietly(() => session.rpc.call('mods.list'));
    target = workspaceMods[0]?.id ?? NEW;
  }

  async function browse() {
    const picked = await session.platform.openFile(`Choose your model for ${asset.name} (.glb from Blender)`, ['glb']);
    if (picked) file = picked;
  }

  async function add() {
    let id = target;
    if (id === NEW) {
      id = newId.trim();
      const created = await tab.safely(() => session.rpc.call('mods.create', { id, name: null, author: null }));
      if (!created) return;
      mods = created; // the new mod is real now, even if adding the model fails below
      target = id;
    }
    const detail = await tab.safely(() => session.rpc.call('mods.replaceModel', { id, file: file.trim(), prefabRef: asset.ref }));
    if (!detail) return;
    const model = detail.models.findLast((m) => m.skin === null); // the model just added comes last
    tab.info(`${asset.name}'s model is now in '${id}' (${model?.lods.length ?? 0} LOD(s)). Install it from the Mods tab.`);
    for (const warning of model?.warnings ?? []) tab.warn(warning);
    open = false;
  }
</script>

{#if !open}
  <button onclick={start}>Replace model in a mod…</button>
{:else}
  <div class="replace" role="group" aria-label="Replace model in a mod">
    <label>
      Mod
      <select aria-label="Mod" bind:value={target}>
        {#each workspaceMods as mod (mod.id)}<option value={mod.id}>{mod.name}</option>{/each}
        <option value={NEW}>New mod…</option>
      </select>
    </label>
    {#if target === NEW}
      <label>New mod id <input aria-label="New mod id" bind:value={newId} /></label>
    {/if}
    <label>Your model <input aria-label="Your model" placeholder="a .glb exported from Blender" bind:value={file} /></label>
    <button onclick={browse}>Browse…</button>
    <button class="primary" disabled={!file.trim() || session.busy} onclick={add}>Add to mod</button>
    <button onclick={() => (open = false)}>Cancel</button>
    <p class="hint">Export the prefab from Tyrant, edit it in Blender (keep the armature, its bone names and the growth shape keys), export a .glb.</p>
  </div>
{/if}

<style>
  .replace { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
  .hint { flex-basis: 100%; margin: 0; }
</style>
