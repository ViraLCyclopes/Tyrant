<script module lang="ts">
  /** The skin each model was last shown wearing, for as long as the app is open: `${mod}|${species}|${skin}` -> skin id ('' = the game's materials). */
  const chosenSkins = new Map<string, string>();
</script>

<script lang="ts">
  import OpenInBlender from '$lib/blender/OpenInBlender.svelte';
  import type { ModelViewer } from '$lib/assets/viewer';
  import { asRpcError } from '$lib/rpc/client';
  import type { ModModelDto, ModModelPreview } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';
  import ModelRigHost from './ModelRigHost.svelte';
  import { askFor, chooseModelRig, createRigAsk } from './modelRig';
  import RigEdit from './RigEdit.svelte';
  import SkinModel3D from './SkinModel3D.svelte';

  /**
   * A replacement model: its 3D preview per LOD, its sizes against the game's, its problems, Replace / Rebuild / Remove, and its
   * rig edit. A species entry with no model file is a rig edit on the game's own mesh.
   */
  let { doc, model, onRemoved = () => {} }: { doc: Pick<ModDoc, 'id' | 'detail' | 'edit'>; model: ModModelDto; onRemoved?: () => void } = $props();
  const session = getSession();
  const rigAsk = createRigAsk();
  const tab = getTab();
  const uid = $props.id();
  let lod = $state(0);
  // The skins that wear this model in game (a skin's own model page: that skin), and the one shown; '' is the template with the game's materials.
  const skins = $derived((doc.detail?.skins ?? []).filter((s) => s.species === model.target && (model.skin ? s.id === model.skin : !s.model)));
  const choiceKey = $derived(`${doc.id}|${model.target}|${model.skin ?? ''}`);
  let choices = $state(0); // bumped when a choice is stored
  const chosen = $derived.by(() => {
    void choices;
    const saved = chosenSkins.get(choiceKey);
    if (saved === '' && skins.length) return '';
    return skins.find((s) => s.id === saved)?.id ?? (skins.find((s) => s.starting) ?? skins[0])?.id ?? '';
  });
  const chosenSkin = $derived(skins.find((s) => s.id === chosen));
  function choose(id: string) {
    chosenSkins.set(choiceKey, id);
    choices++;
  }
  let canvas = $state<HTMLCanvasElement>();
  let viewer = $state.raw<ModelViewer | null>(null);
  let reason = $state<string | null>(null);
  let generation = 0;
  let disposed = false;
  // The preview's files for this build of the model: asked for once, then each LOD button only switches between them.
  let previewKey = '';
  let previewing: Promise<ModModelPreview> | null = null;

  function loadPreview(): Promise<ModModelPreview> {
    const key = `${model.file}|${doc.detail?.revision ?? ''}`;
    if (key !== previewKey || !previewing) {
      previewKey = key;
      previewing = session.rpc.call('mods.modelPreview', { id: doc.id, target: model.target, skin: model.skin });
      previewing.catch(() => (previewKey = '')); // asked again next time
    }
    return previewing;
  }

  async function show(level: number) {
    const mine = ++generation;
    try {
      const preview = await loadPreview();
      const part = preview.lods[Math.min(level, preview.lods.length - 1)];
      if (mine !== generation || disposed || !part) return;
      const module = await import('$lib/assets/viewer');
      if (mine !== generation || disposed) return;
      viewer?.dispose(); // one engine on the canvas at a time
      viewer = null;
      const next = await module.showModels(canvas!, [{ file: part.file, url: session.platform.fileUrl(part.file) }], {
        fileUrl: (path) => session.platform.fileUrl(path),
        materials: preview.materials,
      });
      if (mine !== generation || disposed) {
        next.dispose();
        return;
      }
      viewer = next;
      if (!tab.active) next.pause();
      reason = null;
    } catch (e) {
      if (mine === generation) reason = asRpcError(e).message;
    }
  }

  // The LOD, the model's file or the mod (rebuilds) changed: show it again.
  $effect(() => {
    void model.file;
    void doc.detail?.revision;
    if (chosenSkin) {
      // the skin view has its own canvas: this one is gone
      generation++;
      viewer?.dispose();
      viewer = null;
      reason = null;
    } else if (model.file) void show(lod);
  });
  $effect(() => {
    if (tab.active) viewer?.resume();
    else viewer?.pause();
  });
  $effect(() => () => {
    disposed = true;
    viewer?.dispose();
  });

  // A question asked for one model is not answered for another: opening another model (the page stays) cancels it.
  const destination = $derived(`${model.target}|${model.skin ?? ''}`);
  $effect(() => {
    void destination;
    return () => rigAsk.dispose();
  });

  async function replace() {
    const asked = destination;
    const file = await session.platform.openFile(`Choose your model for ${model.target} (.glb or .fbx)`, ['glb', 'fbx']);
    if (file && asked === destination) await addFile(file, false);
  }

  async function reimport() {
    if (model.origin) await addFile(model.origin, true);
  }

  /** Reads the rig edit in the model, asks about it when it matters, then puts the model in the mod. */
  async function addFile(file: string, reimporting: boolean) {
    // The destination the question is about, read once: a question cancelled by opening another model saves nothing.
    const [asked, skin] = [destination, model.skin];
    const target = skin ? null : model.target;
    const action = reimporting || model.file ? 'Replace' : 'Add';
    const ask = askFor(rigAsk, () => destination === asked);
    const rig = await tab.safely(() => chooseModelRig(session.rpc, { id: doc.id, file, target, skin }, { reimport: reimporting, action }, ask));
    if (rig) await doc.edit('mods.replaceModel', { file, target, skin, rig });
  }

  async function remove() {
    if (await doc.edit('mods.removeModel', { target: model.target, skin: model.skin })) onRemoved();
  }
</script>

<section class="card">
  <h2>Model: {model.target}{model.skin ? ` (skin ${model.skin})` : ''}</h2>
  {#if !model.file}
  <p class="hint">Rig edit only: the game's own mesh with the edited skeleton (it stretches with the moved bones). Add a model made for the rig edit from Blender (Send to Tyrant), or here.</p>
  <div class="row">
    <button onclick={replace}>Add a model…</button>
    <OpenInBlender species={model.target} mod={doc.id} skin={model.skin ?? undefined} />
  </div>
  <ModelRigHost ask={rigAsk} />
  {:else}
  {#if skins.length}
    <div class="row">
      <label for="model-skin-{uid}">Skin</label>
      <select id="model-skin-{uid}" aria-describedby="model-skin-hint-{uid}" value={chosen} onchange={(e) => choose(e.currentTarget.value)}>
        {#each skins as s (s.id)}<option value={s.id}>{s.name}</option>{/each}
        <option value="">Game's materials</option>
      </select>
    </div>
    <p class="hint" id="model-skin-hint-{uid}">In game each skin wears this model with its own maps; maps a skin does not have come from the skin it is based on. Game's materials shows the model with the template's textures only.</p>
  {/if}
  {#if chosenSkin}
    <SkinModel3D {doc} skin={chosenSkin} colorsJson={null} materialsJson={chosenSkin.materialsJson ?? null} variant="normal" {lod} strict />
  {:else}
    {#if reason}<p class="hint">{reason}</p>{/if}
    <canvas bind:this={canvas} class="viewport" class:hidden={!!reason} aria-label="3D preview of the replacement model"></canvas>
  {/if}
  <div class="row">
    {#each model.lods as _, i (i)}
      <button class:on={lod === i} onclick={() => (lod = i)}>LOD {i}</button>
    {/each}
  </div>
  <ul class="stats">
    {#each model.lods as l, i (i)}
      <li>LOD {i}: {l.vertices.toLocaleString('en-US')} vertices (game: {l.vanilla.toLocaleString('en-US')}){l.index32 ? ' · 32-bit indices' : ''}</li>
    {/each}
  </ul>
  {#each model.errors as e (e)}<p class="warn">{e}</p>{/each}
  {#each model.warnings as w (w)}<p class="hint">{w}</p>{/each}
  {#if model.stale}<p class="hint">The model's .glb in the mod changed since it was built; Check and Install build it again.</p>{/if}
  {#if model.originChanged && model.origin}
    <p class="hint">{model.origin} changed since you added it. Re-import it to use the new version.</p>
    <div class="row"><button onclick={reimport}>Re-import</button></div>
  {/if}
  <ModelRigHost ask={rigAsk} />
  <div class="row">
    <button onclick={replace}>Replace…</button>
    <button onclick={() => doc.edit('mods.rebuildModels', {})}>Rebuild LODs</button>
    <button onclick={remove}>Remove</button>
    <OpenInBlender species={model.target} mod={doc.id} skin={model.skin ?? undefined} />
  </div>
  <p class="hint">
    Made in Blender from Tyrant's export: keep the armature, its bone names and the growth shape keys. Sculpt or edit with the Basis selected
    in Shape Keys and the growth keys follow it; do not repeat the change on them, and do not use Voxel Remesh (it deletes them). Name extra meshes …_LOD1 / …_LOD2 to use your own levels of detail; otherwise
    Tyrant makes them. To replace the model, export a .glb or .fbx (use Export model for Tyrant… in Blender's Tyrant panel to keep a rig
    edit's scale).
  </p>
  {/if}
  <RigEdit {doc} target={model.target} skin={model.skin} />
</section>

<style>
  .viewport { width: 100%; height: 320px; display: block; border-radius: var(--radius); background: var(--panel-2); }
  .hidden { display: none; }
  .row { display: flex; gap: 8px; flex-wrap: wrap; margin: 8px 0; }
  .on { background: var(--accent); color: var(--accent-text); }
  .stats { margin: 0; padding-left: 18px; }
</style>
