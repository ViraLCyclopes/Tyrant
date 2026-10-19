<script lang="ts">
  import OpenInBlender from '$lib/blender/OpenInBlender.svelte';
  import type { ModSkinDto } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import NameDialog from './NameDialog.svelte';
  import PortraitsCard from './PortraitsCard.svelte';
  import { groupNames, setGroup } from './skinGroups';
  import ColourEditor from './ColourEditor.svelte';
  import MaterialEditor from './MaterialEditor.svelte';
  import RigEdit from './RigEdit.svelte';
  import type { ModDoc } from './modDoc.svelte';
  import RemoveSkinDialog from './RemoveSkinDialog.svelte';
  import SkinFiles from './SkinFiles.svelte';
  import { checkLinesFor } from './slots';
  import Thumb from './Thumb.svelte';

  let { doc, skin, onOpenModel = () => {} }: { doc: ModDoc; skin: ModSkinDto; onOpenModel?: (target: string, skin: string | null) => void } = $props();
  const session = getSession();
  let name = $state('');
  let removing = $state(false);
  const NEW_GROUP = '__new__';
  const groups = $derived(groupNames(doc.detail?.skins ?? []));
  let namingGroup = $state(false);
  const lines = $derived(checkLinesFor(doc.check, skin.key));
  const speciesModel = $derived(doc.detail?.models.find((m) => m.skin === null && m.target === skin.species));
  /** The Material card's draft, previewed in the Colours card's 3D view; null until it sends one (then what is saved shows). */
  let materialDraft = $state<{ skin: string; json: string | null } | null>(null);
  const draftJson = $derived(materialDraft?.skin === skin.id ? materialDraft.json : null);

  async function replaceModel() {
    const file = await session.platform.openFile(`Choose a model for ${skin.name} (.glb or .fbx)`, ['glb', 'fbx']);
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

  function pickGroup(e: Event & { currentTarget: HTMLSelectElement }) {
    const value = e.currentTarget.value;
    if (value === NEW_GROUP) {
      e.currentTarget.value = skin.group ?? ''; // the dialog decides
      namingGroup = true;
      return;
    }
    void moveTo(value || null);
  }

  async function moveTo(group: string | null) {
    if ((group ?? null) === (skin.group ?? null)) return;
    await doc.edit('mods.arrangeSkins', (d) => ({ order: setGroup(d.skins, [skin.id], group) }));
  }

  async function remove(deleteFiles: boolean) {
    removing = false;
    // Deleted files cannot come back, so undo must not bring back a skin without them.
    await doc.removeSkin(skin.id, deleteFiles);
  }
</script>

<header class="head">
  <label class="name">Skin name <input aria-label="Skin name" bind:value={name} onchange={rename} /></label>
  <label class="group">Group
    <select aria-label="Group" value={skin.group ?? ''} onchange={pickGroup}>
      <option value="">No group</option>
      {#each groups as g (g)}<option value={g}>{g}</option>{/each}
      <option value={NEW_GROUP}>New group…</option>
    </select>
  </label>
  {#if skin.starting}
    <p class="hint">Starting skin of {skin.species}{skin.copiedFromSpecies ? ` · copied from ${skin.copiedFromSpecies}` : ''} · id <code>{skin.id}</code> (saved animals use it, so it never changes)</p>
  {:else}
    <p class="hint">{skin.species} · based on {skin.base} · id <code>{skin.id}</code> (saved animals use it, so it never changes)</p>
  {/if}
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

<PortraitsCard {doc} target={{ skin }} />

<section class="card">
  <h2>Model</h2>
  <div class="row">
    {#if skin.model}
      <span>Model: its own</span>
      <button onclick={() => onOpenModel(skin.species, skin.id)}>Open</button>
      <button onclick={replaceModel}>Replace model…</button>
      <button onclick={() => doc.edit('mods.removeModel', { target: skin.species, skin: skin.id })}>Use species model</button>
    {:else}
      <span>{skin.rig ? "Model: the game's, with this skin's rig edit" : speciesModel?.file ? `Model: species replacement (${speciesModel.file})` : speciesModel?.rig ? "Model: the game's, with the species' rig edit" : "Model: the game's"}</span>
      <button onclick={replaceModel}>Replace model…</button>
    {/if}
    <OpenInBlender species={skin.species} mod={doc.id} skin={skin.id} />
  </div>
  <RigEdit {doc} target={skin.species} skin={skin.id} />
</section>

<section class="card">
  <h2>Files</h2>
  <SkinFiles {doc} {skin} sex="male" />
  <SkinFiles {doc} {skin} sex="female" />
</section>

<section class="card">
  <h2>Colours</h2>
  <ColourEditor {doc} {skin} materialsJson={draftJson} />
</section>

<section class="card">
  <h2>Material</h2>
  <MaterialEditor {doc} {skin} onDraft={(json) => (materialDraft = { skin: skin.id, json })} />
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
  <RemoveSkinDialog name={skin.name} startingOf={skin.starting ? skin.species : null} onCancel={() => (removing = false)} onConfirm={remove} />
{/if}

{#if namingGroup}
  <NameDialog title="New group" label="Group name" action="Move" onCancel={() => (namingGroup = false)}
    onConfirm={(name) => { namingGroup = false; void moveTo(name); }} />
{/if}

<style>
  .head { display: grid; gap: 4px; }
  .head p { margin: 0; }
  .name { display: flex; gap: 8px; align-items: center; font-weight: 600; }
  .group { display: flex; gap: 8px; align-items: center; }
  .name input { font-size: 18px; min-width: 260px; }
  .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
  .card { display: grid; gap: 10px; }
  .card h2 { margin: 0; }
</style>
