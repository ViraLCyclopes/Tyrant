<script lang="ts">
  import type { AssetRow, ModsListResult } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';

  let { asset }: { asset: AssetRow } = $props();

  const NEW = '__new__';
  const session = getSession();
  let open = $state(false);
  let mods = $state<ModsListResult | null>(null);
  let target = $state(NEW);
  let newId = $state('my-mod');
  let png = $state('');
  const workspaceMods = $derived((mods?.mods ?? []).filter((m) => m.state !== 'gameOnly'));

  async function start() {
    open = true;
    mods = await session.quietly(() => session.rpc.call('mods.list'));
    target = workspaceMods[0]?.id ?? NEW;
  }

  async function browse() {
    const picked = await session.platform.openFile(`Choose your PNG for ${asset.name}`, ['png']);
    if (picked) png = picked;
  }

  async function add() {
    let id = target;
    if (id === NEW) {
      id = newId.trim();
      const created = await session.safely(() => session.rpc.call('mods.create', { id, name: null, author: null }));
      if (!created) return;
      mods = created; // the new mod is real now, even if adding the texture fails below
      target = id;
    }
    const r = await session.safely(() => session.rpc.call('mods.replace', { id, texture: asset.ref, png: png.trim() || null }));
    if (!r) return;
    session.notice = `Added ${asset.name} to '${id}'. Install it from the Mods tab.`;
    open = false;
  }
</script>

{#if !open}
  <button onclick={start}>Replace in a mod…</button>
{:else}
  <div class="replace" role="group" aria-label="Replace in a mod">
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
    <label>Your PNG <input aria-label="Your PNG" placeholder="empty: the exported PNG" bind:value={png} /></label>
    <button onclick={browse}>Browse…</button>
    <button class="primary" onclick={add} disabled={session.busy}>Add to mod</button>
    <button class="ghost" onclick={() => (open = false)}>Cancel</button>
    <p class="hint">Leave "Your PNG" empty to use the PNG you exported from here (under assets\textures), after editing it.</p>
  </div>
{/if}

<style>
  .replace { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-top: 8px; }
  .replace .hint { flex-basis: 100%; }
</style>
