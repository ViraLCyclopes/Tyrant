<script lang="ts">
  import { onMount } from 'svelte';
  import { getBlender } from '$lib/blender/blender.svelte';
  import type { AnimationInfo } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  /** A species' animations in the game: search, tick, export them as FBX (default) or glb, or open them in Blender. */
  let { speciesKey, displayName }: { speciesKey: string; displayName: string } = $props();
  const session = getSession();
  const tab = getTab();
  const blender = getBlender(session);
  /** Above this many, ticking them all asks first (each is a file of a few MB when exported). */
  const MANY = 50;
  let species = $state('');
  let animations = $state<AnimationInfo[] | null>(null);
  let failed = $state(false);
  let search = $state('');
  let picked = $state<string[]>([]);
  let format = $state<'fbx' | 'glb' | 'both'>('fbx');
  let singleFile = $state(false);
  let lastExport = $state<string | null>(null);
  const shown = $derived((animations ?? []).filter((a) => a.name.toLowerCase().includes(search.trim().toLowerCase())));

  onMount(async () => {
    const r = await tab.safely(() => session.rpc.call('species.animations', { species: speciesKey }));
    if (r) {
      species = r.species;
      animations = r.animations;
    } else failed = true;
  });

  function toggle(id: string) {
    picked = picked.includes(id) ? picked.filter((p) => p !== id) : [...picked, id];
  }

  async function selectAll() {
    const ids = shown.map((a) => a.id);
    if (ids.length > MANY && !(await session.platform.confirm(
      `Tick all ${ids.length} animations? Exporting them writes ${ids.length} files and takes a while.`, 'Select all'))) return;
    picked = [...new Set([...picked, ...ids])];
  }

  async function exportPicked() {
    const r = await session.runJob('species.exportAnimations', { species, ids: picked, format, singleFile }, `Animations: ${displayName}`, tab);
    if (r) lastExport = r.directory;
  }

  function openPicked() {
    void blender.open(tab, { species, skin: null, mod: null, fresh: false, lods: false, sex: 'male', prefabRef: null, ik: true, animations: picked });
  }

  function seconds(a: AnimationInfo) {
    const flags = [a.loops ? 'loops' : null, a.travels ? 'travels' : null].filter(Boolean);
    return `${a.length.toFixed(2)} s${flags.length ? ` · ${flags.join(' · ')}` : ''}`;
  }
</script>

<section class="animations" aria-label="Animations of {displayName}">
  <h3>Animations: {displayName}</h3>
  {#if failed}
    <p class="hint">The animations could not be read (see the log).</p>
  {:else if animations === null}
    <p class="hint">Reading the game's animations…</p>
  {:else if animations.length === 0}
    <p class="hint">{displayName} has no animations in the game data.</p>
  {:else}
    <div class="row">
      <input type="search" placeholder="Search" aria-label="Search animations" bind:value={search} />
      <button onclick={selectAll}>Select all</button>
      <button onclick={() => (picked = [])} disabled={picked.length === 0}>Clear</button>
      <span class="hint">{picked.length} of {animations.length} ticked</span>
    </div>
    <ul>
      {#each shown as a (a.id)}
        <li>
          <label>
            <input type="checkbox" aria-label="Pick {a.name}" checked={picked.includes(a.id)} onchange={() => toggle(a.id)} />
            <span>{a.name}</span>
          </label>
          <span class="hint">{seconds(a)}</span>
        </li>
      {/each}
    </ul>
    <div class="row">
      <label>Format
        <select bind:value={format}>
          <option value="fbx">FBX</option>
          <option value="glb">glb</option>
          <option value="both">FBX + glb</option>
        </select>
      </label>
      <label><input type="checkbox" bind:checked={singleFile} /> All in one file</label>
      <button onclick={exportPicked} disabled={picked.length === 0 || session.busy}>Export animations…</button>
      <button onclick={openPicked} disabled={picked.length === 0 || session.busy}>Open in Blender with these</button>
    </div>
    {#if lastExport}
      <p class="hint">Exported to {lastExport}. <button class="link" onclick={() => session.platform.reveal(lastExport!)}>Show</button></p>
    {/if}
    <p class="hint">
      FBX files hold the model with each animation as a take (Blender, Better FBX, Maya, Max and Unity open them). In Blender, the
      Tyrant panel's Animations box adds more to an open model.
    </p>
  {/if}
</section>

<style>
  .animations { margin-top: 12px; }
  ul { margin: 4px 0; padding-left: 0; list-style: none; max-height: 320px; overflow: auto; }
  li { display: flex; justify-content: space-between; gap: 12px; }
  .row { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin: 6px 0; }
  .link { background: none; border: none; padding: 0; color: var(--accent); cursor: pointer; }
</style>
