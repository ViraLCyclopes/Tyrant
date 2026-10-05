<script lang="ts">
  import type { ModSkinDto } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import ColourEditor from './ColourEditor.svelte';
  import type { ModDoc } from './modDoc.svelte';
  import RemoveSkinDialog from './RemoveSkinDialog.svelte';
  import SkinFiles from './SkinFiles.svelte';
  import { checkLinesFor } from './slots';
  import Thumb from './Thumb.svelte';

  let { doc, skin, onOpenModel = () => {} }: { doc: ModDoc; skin: ModSkinDto; onOpenModel?: (target: string, skin: string | null) => void } = $props();
  const session = getSession();
  let name = $state('');
  let removing = $state(false);
  const lines = $derived(checkLinesFor(doc.check, skin.key));
  const speciesModel = $derived(doc.detail?.models.find((m) => m.skin === null && m.target === skin.species));

  async function replaceModel() {
    const file = await session.platform.openFile(`Choose a model for ${skin.name} (.glb from Blender)`, ['glb']);
    if (file) await doc.edit('mods.replaceModel', { file, skin: skin.id });
  }

  // Follows saves, undo and outside changes.
  $effect(() => {
    name = skin.name;
  });

  async function rename() {
    const wanted = name.trim();
    if (wanted && wanted !== skin.name) await doc.edit('mods.renameSkin', { skin: skin.id, name: wanted });
    else name = skin.name;
  }

  async function pickThumbnail() {
    const png = await session.platform.openFile('Choose a small PNG for the skin swatch', ['png']);
    if (png) await doc.edit('mods.setThumbnail', { skin: skin.id, png });
  }

  async function remove(deleteFiles: boolean) {
    removing = false;
    // Deleted files cannot come back, so undo must not bring back a skin without them.
    await doc.edit('mods.removeSkin', { skin: skin.id, deleteFiles }, { undoable: !deleteFiles });
  }
</script>

<header class="head">
  <label class="name">Skin name <input aria-label="Skin name" bind:value={name} onchange={rename} /></label>
  <p class="hint">{skin.species} · based on {skin.base} · id <code>{skin.id}</code> (saved animals use it, so it never changes)</p>
</header>

<section class="card">
  <h2>Swatch</h2>
  <div class="row">
    <Thumb modId={doc.id} file={skin.thumbnail} size={64} label="Swatch of {skin.name}" version="{doc.detail?.revision}|{doc.detail?.filesStamp}" />
    <button onclick={pickThumbnail}>Pick PNG…</button>
    <button disabled={!skin.thumbnail} onclick={() => doc.edit('mods.setThumbnail', { skin: skin.id, png: null })}>Automatic</button>
    {#if !skin.thumbnail}<span class="hint">The game cuts a swatch from the diffuse.</span>{/if}
  </div>
</section>

<section class="card">
  <h2>Model</h2>
  <div class="row">
    {#if skin.model}
      <span>Model: its own</span>
      <button onclick={() => onOpenModel(skin.species, skin.id)}>Open</button>
      <button onclick={replaceModel}>Replace model…</button>
      <button onclick={() => doc.edit('mods.removeModel', { target: skin.species, skin: skin.id })}>Use species model</button>
    {:else}
      <span>{speciesModel ? `Model: species replacement (${speciesModel.file})` : "Model: the game's"}</span>
      <button onclick={replaceModel}>Replace model…</button>
    {/if}
  </div>
</section>

<section class="card">
  <h2>Files</h2>
  <SkinFiles {doc} {skin} sex="male" />
  <SkinFiles {doc} {skin} sex="female" />
</section>

<section class="card">
  <h2>Colours</h2>
  <ColourEditor {doc} {skin} />
</section>

{#if lines.errors.length || lines.warnings.length}
  <section class="card" aria-label="Check results for this skin">
    <h2>Check</h2>
    {#each lines.errors as line (line)}<p class="warn">{line}</p>{/each}
    {#each lines.warnings as line (line)}<p class="hint">{line}</p>{/each}
  </section>
{/if}

<div class="row"><button onclick={() => (removing = true)}>Remove skin…</button></div>

{#if removing}
  <RemoveSkinDialog name={skin.name} onCancel={() => (removing = false)} onConfirm={remove} />
{/if}

<style>
  .head { display: grid; gap: 4px; }
  .head p { margin: 0; }
  .name { display: flex; gap: 8px; align-items: center; font-weight: 600; }
  .name input { font-size: 18px; min-width: 260px; }
  .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
  .card { display: grid; gap: 10px; }
  .card h2 { margin: 0; }
</style>
