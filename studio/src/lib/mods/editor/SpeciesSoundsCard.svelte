<script lang="ts">
  import type { AddedSound } from '$lib/sounds/audio';
  import type { ModDoc } from './modDoc.svelte';

  /**
   * The species page's Sounds: this mod's replacements for the species and for its skins, each with its files, volume, Edit
   * (its own page) and Remove; + Add opens the sound picker on this species. Other sounds keep playing the template's.
   */
  let { doc, species, onOpen, onAdd }: {
    doc: Pick<ModDoc, 'detail' | 'edit'>;
    /** The species' id as a sound replacement names it ("tyrant.<mod>.<id>"). */
    species: string;
    onOpen: (sound: AddedSound) => void;
    onAdd: () => void;
  } = $props();

  const skins = $derived(new Map((doc.detail?.skins ?? []).filter((s) => s.species === species).map((s) => [s.key, s.name])));
  const sounds = $derived((doc.detail?.sounds ?? []).filter((s) => s.species === species || (s.skin !== null && skins.has(s.skin))));
  const baseName = (file: string) => file.split(/[\\/]/).pop() ?? file;
</script>

<section class="sounds" aria-label="Sounds">
  <h3>Sounds</h3>
  {#if sounds.length}
    <ul aria-label="Sound replacements">
      {#each sounds as sound (`${sound.event}|${sound.species}|${sound.skin}`)}
        <li>
          <span class="name" title={sound.event}>{sound.name}</span>
          {#if sound.skin}<span class="hint">{skins.get(sound.skin)} skin</span>{/if}
          <span class="files">{sound.files.map(baseName).join(', ')}</span>
          <span class="hint">volume {sound.volume.toFixed(2)}</span>
          <button type="button" class="small" aria-label="Edit {sound.name}" onclick={() => onOpen({ event: sound.event, species: sound.species, skin: sound.skin })}>Edit</button>
          <button type="button" class="small" aria-label="Remove {sound.name}"
            onclick={() => doc.edit('mods.removeSound', { event: sound.event, species: sound.species, skin: sound.skin })}>Remove</button>
        </li>
      {/each}
    </ul>
  {:else}
    <p class="hint">The species plays its template's sounds. Replace one for this species alone with + Add.</p>
  {/if}
  <div><button type="button" aria-label="Add a sound for this species" onclick={onAdd}>+ Add</button></div>
</section>

<style>
  .sounds { display: grid; gap: 8px; }
  h3, p { margin: 0; }
  ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
  li { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
  .name { min-width: 160px; }
  .files { font-family: var(--mono); font-size: 12px; }
  .hint { font-size: 12px; }
  .small { padding: 2px 8px; font-size: 0.85em; }
</style>
