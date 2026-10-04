<script lang="ts">
  import type { ModsListResult, SoundDto } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import { AUDIO_EXTENSIONS, type AddedSound } from './audio';

  /**
   * Replace… on a game sound: the user's audio files play instead of it, in a mod. From a species' list the replacement can be
   * that species' own (speciesId) or for every animal that uses the sound; from All sounds it is for everyone. modId: add to
   * this mod (the mod editor) without asking which; onAdded: told which replacement was added.
   */
  let {
    sound,
    speciesId = null,
    modId,
    onAdded,
  }: { sound: SoundDto; speciesId?: string | null; modId?: string; onAdded?: (added: AddedSound) => void } = $props();

  const NEW = '__new__';
  const session = getSession();
  const tab = getTab();
  let open = $state(false);
  let mods = $state<ModsListResult | null>(null);
  let target = $state(NEW);
  let newId = $state('my-mod');
  let files = $state<string[]>([]);
  let onlySpecies = $state(true);
  const workspaceMods = $derived((mods?.mods ?? []).filter((m) => m.state !== 'gameOnly'));
  const fileNames = $derived(files.map((f) => f.split(/[\\/]/).pop()).join(', '));
  /** Only sounds an animal plays can be one species' own; menu sounds are replaced for everyone. */
  const canBeUnique = $derived(speciesId !== null && sound.perAnimal);
  const unique = $derived(canBeUnique && onlySpecies);

  async function start() {
    open = true;
    if (modId) return;
    mods = await tab.quietly(() => session.rpc.call('mods.list'));
    target = workspaceMods[0]?.id ?? NEW;
  }

  async function choose() {
    const picked = await session.platform.openFiles(`Choose the new sound for ${sound.name} (WAV, OGG, MP3 or FLAC; several = one at random)`, AUDIO_EXTENSIONS);
    if (picked.length) files = picked;
  }

  async function add() {
    let id = modId ?? target;
    if (!modId && id === NEW) {
      id = newId.trim();
      const created = await tab.safely(() => session.rpc.call('mods.create', { id, name: null, author: null }));
      if (!created) return;
      mods = created; // the new mod is real now, even if adding the sound fails below
      target = id;
    }
    const species = unique ? speciesId : null;
    const detail = await tab.safely(() => session.rpc.call('mods.replaceSound', { id, event: sound.event, files, species, skin: null }));
    if (!detail) return;
    tab.info(`${sound.name} is now replaced in '${id}' ${species ? `for ${species}` : 'for everyone'}. Install it from the Mods tab.`);
    const others = sound.species.filter((s) => s !== speciesId);
    if (!species && others.length)
      tab.warn(`${others.slice(0, 5).join(', ')}${others.length > 5 ? ` and ${others.length - 5} more` : ''} ${others.length === 1 ? 'hears' : 'hear'} it too. Choose Only ${speciesId ?? 'one species'} to change it for one animal.`);
    open = false;
    files = [];
    onAdded?.({ event: sound.event, species, skin: null });
  }
</script>

{#if !open}
  <button aria-label="Replace {sound.name}" onclick={start}>Replace…</button>
{:else}
  <div class="replace" role="group" aria-label="Replace {sound.name} in a mod">
    {#if !modId}
      <label>
        Mod
        <select aria-label="Mod" bind:value={target}>
          {#each workspaceMods as mod (mod.id)}<option value={mod.id}>{mod.name}</option>{/each}
          <option value={NEW}>New mod…</option>
        </select>
      </label>
    {/if}
    {#if !modId && target === NEW}
      <label>New mod id <input aria-label="New mod id" bind:value={newId} /></label>
    {/if}
    <button onclick={choose}>Choose files…</button>
    {#if files.length}<span class="files">{fileNames}</span>{/if}
    {#if speciesId && !sound.perAnimal}
      <p class="hint">This sound plays in menus (the Nursery, the Paleopedia), not from the animal, so it is replaced for everyone.</p>
    {/if}
    {#if canBeUnique}
      <fieldset class="scope">
        <legend class="visually-hidden">Who hears it</legend>
        <label><input type="radio" name="scope-{sound.event}" checked={onlySpecies} onchange={() => (onlySpecies = true)} /> Only {speciesId}</label>
        <label><input type="radio" name="scope-{sound.event}" checked={!onlySpecies} onchange={() => (onlySpecies = false)} /> For every animal that uses it</label>
      </fieldset>
    {/if}
    <button class="primary" disabled={!files.length || session.busy} onclick={add}>Add to mod</button>
    <button onclick={() => (open = false)}>Cancel</button>
    {#if sound.species.length > 1}<p class="hint">Shared by {sound.species.length} species.</p>{/if}
  </div>
{/if}

<style>
  .replace { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; padding: 6px 0; }
  .files { font-family: var(--mono); font-size: 12px; }
  .scope { display: flex; gap: 12px; border: none; margin: 0; padding: 0; }
  .hint { flex-basis: 100%; margin: 0; }
</style>
