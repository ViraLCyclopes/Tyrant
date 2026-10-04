<script lang="ts">
  import type { ModSoundDto } from '$lib/rpc/types.gen';
  import { AUDIO_EXTENSIONS } from '$lib/sounds/audio';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /** A sound replacement: who hears it (one species, one skin or everyone), its files, volume and baby pitch, and Remove. */
  let {
    doc,
    sound,
    onRemoved = () => {},
    onScopeChanged = () => {},
  }: {
    doc: Pick<ModDoc, 'id' | 'detail' | 'edit'>;
    sound: ModSoundDto;
    onRemoved?: () => void;
    onScopeChanged?: (species: string | null, skin: string | null) => void;
  } = $props();
  const session = getSession();

  type Scope = 'species' | 'skin' | 'everyone';
  const scopeOf = (s: ModSoundDto): Scope => (s.skin ? 'skin' : s.species ? 'species' : 'everyone');
  let scope = $state<Scope>('everyone');
  let speciesText = $state('');
  let skinText = $state('');
  // Shown first, another sound, or the same one after a move: start from what it has.
  $effect.pre(() => {
    scope = scopeOf(sound);
    speciesText = sound.species ?? '';
    skinText = sound.skin ?? '';
  });

  const which = $derived({ event: sound.event, species: sound.species, skin: sound.skin });
  const skinKeys = $derived((doc.detail?.skins ?? []).map((s) => s.key));
  const changed = $derived(
    scope !== scopeOf(sound)
      || (scope === 'species' && speciesText.trim() !== (sound.species ?? ''))
      || (scope === 'skin' && skinText.trim() !== (sound.skin ?? '')),
  );
  const ready = $derived(scope === 'everyone' || (scope === 'species' ? !!speciesText.trim() : !!skinText.trim()));
  const baseName = (file: string) => file.split(/[\\/]/).pop() ?? file;

  /** The mod's file as a full path: sent back as it is, the mod keeps it (no copy). */
  function inMod(file: string): string {
    const dir = doc.detail?.dir ?? '';
    const separator = dir.includes('\\') ? '\\' : '/';
    return `${dir}${separator}${file.replaceAll('/', separator)}`;
  }

  async function applyScope() {
    const move = scope === 'everyone' ? { forEveryone: true } : scope === 'species' ? { newSpecies: speciesText.trim() } : { newSkin: skinText.trim() };
    if (await doc.edit('mods.setSound', { ...which, ...move }))
      onScopeChanged(scope === 'species' ? speciesText.trim() : null, scope === 'skin' ? skinText.trim() : null);
  }

  async function addFiles() {
    const picked = await session.platform.openFiles(`Add sounds for ${sound.name} (one is picked at random each time)`, AUDIO_EXTENSIONS);
    if (picked.length) await doc.edit('mods.replaceSound', { ...which, files: [...sound.files.map(inMod), ...picked] });
  }

  async function removeFile(file: string) {
    await doc.edit('mods.replaceSound', { ...which, files: sound.files.filter((f) => f !== file).map(inMod) });
  }

  async function remove() {
    if (await doc.edit('mods.removeSound', which)) onRemoved();
  }
</script>

<section class="card">
  <h2>Sound: {sound.name}</h2>
  <p class="path">{sound.event}</p>
  <p class="hint">{sound.group}</p>

  <fieldset>
    <legend>Who hears it</legend>
    <label><input type="radio" name="sound-scope" checked={scope === 'species'} onchange={() => (scope = 'species')} /> Only one species</label>
    {#if scope === 'species'}<input aria-label="Species" placeholder="e.g. Carcharodontosaurus" bind:value={speciesText} />{/if}
    <label><input type="radio" name="sound-scope" checked={scope === 'skin'} onchange={() => (scope = 'skin')} /> Only one skin</label>
    {#if scope === 'skin'}
      <input aria-label="Skin" placeholder="mod/skin or Species/Skin name" list="sound-skin-keys" bind:value={skinText} />
      <datalist id="sound-skin-keys">{#each skinKeys as key (key)}<option value={key}></option>{/each}</datalist>
    {/if}
    <label><input type="radio" name="sound-scope" checked={scope === 'everyone'} onchange={() => (scope = 'everyone')} /> Everyone</label>
    <button disabled={!changed || !ready} onclick={applyScope}>Apply</button>
    <p class="hint">A species' or skin's own sound wins over the one for everyone, even from another mod.</p>
  </fieldset>

  <h3>Files</h3>
  <ul class="files">
    {#each sound.files as file (file)}
      <li><span>{baseName(file)}</span> <button aria-label="Remove {baseName(file)}" disabled={sound.files.length === 1} onclick={() => removeFile(file)}>✕</button></li>
    {/each}
  </ul>
  <div class="row"><button onclick={addFiles}>Add files…</button></div>
  {#if sound.files.length > 1}<p class="hint">One of these plays at random each time (never the same one twice in a row).</p>{/if}

  <label class="slider">
    Volume
    <input type="range" aria-label="Volume" min="0" max="2" step="0.05" value={sound.volume}
      onchange={(e) => doc.edit('mods.setSound', { ...which, volume: Number(e.currentTarget.value) })} />
    <span class="number">{sound.volume.toFixed(2)}</span>
  </label>
  <label class="slider">
    Baby pitch
    <input type="range" aria-label="Baby pitch" min="0" max="1" step="0.05" value={sound.agePitch}
      onchange={(e) => doc.edit('mods.setSound', { ...which, agePitch: Number(e.currentTarget.value) })} />
    <span class="number">{sound.agePitch.toFixed(2)}</span>
  </label>
  <p class="hint">Baby pitch: how much higher young animals play this sound (0 = like adults). Interface sounds and music ignore it.</p>

  <div class="row"><button onclick={remove}>Remove</button></div>
</section>

<style>
  .path { font-family: var(--mono); font-size: 12px; word-break: break-all; margin: 0; }
  fieldset { display: grid; gap: 6px; border: 1px solid var(--border); border-radius: var(--radius); padding: 8px 12px; margin: 10px 0; justify-items: start; }
  .files { list-style: none; margin: 0; padding: 0; }
  .files li { display: flex; gap: 8px; align-items: center; font-family: var(--mono); font-size: 12px; }
  .row { display: flex; gap: 8px; flex-wrap: wrap; margin: 8px 0; }
  .slider { display: flex; gap: 10px; align-items: center; margin: 6px 0; }
  .slider input { width: 200px; }
  .number { font-family: var(--mono); font-size: 12px; color: var(--muted); }
</style>
