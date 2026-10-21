<script lang="ts">
  import { onMount } from 'svelte';
  import type { CustomSpeciesRow, ModSkinDto, ModSpeciesResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import AllSounds from '$lib/sounds/AllSounds.svelte';
  import type { AddedSound } from '$lib/sounds/audio';
  import ReplaceFromFolder from '$lib/sounds/ReplaceFromFolder.svelte';
  import SpeciesSounds from '$lib/sounds/SpeciesSounds.svelte';

  /**
   * The mod editor's Sounds → + Add: a species' sounds (or every game sound), each with Replace… into this mod; for a species,
   * Replace from folder replaces a sound pack's matched files at once (onReplaceMany: one edit of the editor's). The mod's own
   * species are listed too, by name: they play their template's sounds, and a replacement for one is for it alone.
   */
  let { modId, onDone, onReplaceMany = undefined, modSkins = [], species = '' }: {
    modId: string;
    onDone: (added: AddedSound | null) => void;
    onReplaceMany?: (species: string, skin: string | null, sounds: { event: string; files: string[] }[]) => Promise<boolean>;
    /** This mod's own skins: a folder of sounds can be for one of them too. */
    modSkins?: ModSkinDto[];
    /** The species to open on (the species page's + Add). */
    species?: string;
  } = $props();

  const ALL = '__all__';
  const session = getSession();
  const tab = getTab();
  let data = $state.raw<ModSpeciesResult | null>(null);
  let own = $state.raw<CustomSpeciesRow[]>([]);
  let source = $state('');
  const nameOf = (id: string) => own.find((c) => c.speciesId === id)?.name ?? id;

  /** The species' game skins and this mod's skins of it, as the keys a sound replacement names a skin by. */
  function skinsOf(speciesId: string): { key: string; label: string }[] {
    const game = (data?.species.find((s) => s.speciesId === speciesId)?.skins ?? []).map((s) => ({ key: `${speciesId}/${s.name}`, label: s.name }));
    const mine = modSkins.filter((s) => s.species === speciesId).map((s) => ({ key: s.key, label: s.name }));
    return [...game, ...mine];
  }

  onMount(async () => {
    const [speciesList, custom] = await Promise.all([
      tab.quietly(() => session.rpc.call('mods.species')),
      tab.quietly(() => session.rpc.call('mods.customSpecies', { id: modId })),
    ]);
    own = custom?.species.filter((c) => c.own) ?? [];
    data = speciesList;
    source = species;
  });
</script>

<section class="card">
  <h2>Add a sound</h2>
  {#if !data}
    <p class="hint">Loading the species…</p>
  {:else if !data.hasDump}
    <p class="hint">The sound lists need the game's data: on the Workspace tab click Run data dump, then try again.</p>
  {:else}
    <label>
      Sounds of
      <select aria-label="Sounds of" bind:value={source}>
        <option value="" disabled>Pick a species…</option>
        <optgroup label="Base game">
          {#each data.species as s (s.speciesId)}<option value={s.speciesId}>{s.speciesId}</option>{/each}
        </optgroup>
        {#if own.length}
          <optgroup label="This mod's species">
            {#each own as c (c.speciesId)}<option value={c.speciesId}>{c.name}</option>{/each}
          </optgroup>
        {/if}
        <option value={ALL}>All sounds (buttons, buildings, music…)</option>
      </select>
    </label>
    {#if source === ALL}
      <AllSounds {modId} onAdded={onDone} />
    {:else if source}
      {#key source}
        {#if onReplaceMany}
          {@const replaceMany = onReplaceMany}
          <ReplaceFromFolder species={source} name={nameOf(source)} skins={skinsOf(source)} onReplace={(sounds, skin) => replaceMany(source, skin, sounds)} />
        {/if}
        <SpeciesSounds speciesKey={source} displayName={nameOf(source)} {modId} onAdded={onDone} />
      {/key}
    {/if}
  {/if}
  <div class="row"><button onclick={() => onDone(null)}>Cancel</button></div>
</section>

<style>
  .row { display: flex; gap: 8px; margin-top: 8px; }
</style>
