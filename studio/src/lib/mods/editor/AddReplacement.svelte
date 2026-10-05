<script lang="ts">
  import { onMount } from 'svelte';
  import type { AssetRow, ModSpeciesResult, SpeciesTextureDto } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';

  /** The mod editor's Texture replacements → + Add: a species' skin textures (or any game texture), each with Replace… into this mod. */
  let { modId, onDone }: { modId: string; onDone: (texture: string | null) => void } = $props();

  const ANY = '__any__';
  const session = getSession();
  const tab = getTab();
  let data = $state.raw<ModSpeciesResult | null>(null);
  let source = $state('');
  let textures = $state.raw<SpeciesTextureDto[] | null>(null);
  let text = $state('');
  let found = $state.raw<string[]>([]);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let generation = 0;

  onMount(() => {
    void (async () => (data = await tab.quietly(() => session.rpc.call('mods.species'))))();
    return () => clearTimeout(timer);
  });

  async function pick() {
    textures = null;
    found = [];
    if (!source || source === ANY) return;
    const mine = ++generation;
    const result = await tab.quietly(() => session.rpc.call('mods.speciesTextures', { species: source }));
    if (result && mine === generation) textures = result.textures;
  }

  function typed() {
    clearTimeout(timer);
    timer = setTimeout(() => void search(), 250);
  }

  async function search() {
    const query = text.trim();
    if (!query) {
      found = [];
      return;
    }
    const mine = ++generation;
    const result = await tab.quietly(() => session.rpc.call('assets.list', { filter: query, type: 'Texture2D', page: 0, pageSize: 100 }));
    if (result && mine === generation) found = [...new Set(result.rows.map((r: AssetRow) => r.name))];
  }

  async function replace(texture: string) {
    const png = await session.platform.openFile(`Choose the new PNG for ${texture}`, ['png']);
    if (!png) return;
    const done = await tab.safely(() => session.rpc.call('mods.replace', { id: modId, texture, png }));
    if (done) onDone(texture);
  }
</script>

<section class="card">
  <h2>Replace a texture</h2>
  {#if !data}
    <p class="hint">Loading the species…</p>
  {:else}
    <label>
      Textures of
      <select aria-label="Textures of" bind:value={source} onchange={pick}>
        <option value="" disabled>Pick a species…</option>
        {#each data.species as s (s.speciesId)}<option value={s.speciesId}>{s.speciesId}</option>{/each}
        <option value={ANY}>Any game texture (search)…</option>
      </select>
    </label>
    {#if !data.hasDump && source !== ANY}
      <p class="hint">Species lists need the game's data: on the Workspace tab click Run data dump. You can still search any texture.</p>
    {/if}
    {#if source === ANY}
      <input type="search" placeholder="Part of a texture name, e.g. fence or T_allosaurus" aria-label="Find a texture" bind:value={text} oninput={typed} />
      <ul>
        {#each found as texture (texture)}
          <li><span class="name">{texture}</span> <button aria-label="Replace {texture}" onclick={() => replace(texture)}>Replace…</button></li>
        {/each}
      </ul>
    {:else if textures}
      <ul>
        {#each textures as t (t.texture)}
          <li>
            <span class="name" title={t.skins.join(', ')}>{t.texture}</span>
            <span class="slot">{t.slot}</span>
            {#if t.sharedWith.length}
              <span class="shared" title={t.sharedWith.join(', ')}>also used by {t.sharedWith.length} other species</span>
            {/if}
            <button aria-label="Replace {t.texture}" onclick={() => replace(t.texture)}>Replace…</button>
          </li>
        {:else}
          <li class="hint">No skin textures found for this species.</li>
        {/each}
      </ul>
      <p class="hint">
        A replacement swaps the texture wherever the game uses it (every skin and species listed). Extra maps: R = smoothness (above
        0.9 marks the eyes), G = ambient occlusion. Pattern masks: R = where the game recolours (black keeps your colours), G = the
        secondary colour.
      </p>
    {/if}
  {/if}
  <div class="row"><button onclick={() => onDone(null)}>Cancel</button></div>
</section>

<style>
  ul { list-style: none; margin: 8px 0; padding: 0; max-height: 420px; overflow: auto; }
  li { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; padding: 3px 0; border-bottom: 1px solid var(--border); }
  .name { font-family: var(--mono); font-size: 12px; min-width: 260px; }
  .slot { color: var(--muted); min-width: 110px; }
  .shared { color: var(--warn-text); font-size: 12px; }
  .row { display: flex; gap: 8px; margin-top: 8px; }
</style>
