<script lang="ts">
  import { untrack } from 'svelte';
  import type { ModelViewer } from '$lib/assets/viewer';
  import { iridescenceUniforms } from '$lib/assets/viewerMaterials';
  import { asRpcError } from '$lib/rpc/client';
  import type { ModSkinDto, ModSkinOwnModel } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { Variant } from './colors';
  import type { ModDoc } from './modDoc.svelte';

  /**
   * One animal of the skin in 3D: the model it wears (its own, the mod's species model, or the game's) with the skin's maps,
   * the 2D strip's first animal's colours and its material (iridescence): `colorsJson` and `materialsJson` are the editor's drafts. Null colours mean the saved ones, but null materials mean none (the server then reads only the starting skin's block and the species'), so a page without an editor passes the skin's saved `materialsJson`.
   * The model page passes `lod` (the level of detail to show) and `strict` (a model that cannot be shown says why instead of showing the game's).
   */
  let {
    doc, skin, colorsJson, variant, materialsJson = null, lod, strict = false,
  }: {
    doc: Pick<ModDoc, 'id' | 'detail'>; skin: ModSkinDto; colorsJson: string | null; variant: Variant; materialsJson?: string | null; lod?: number; strict?: boolean;
  } = $props();
  const session = getSession();
  const tab = getTab();
  type Sex = 'male' | 'female' | 'infant';
  const SEXES: [Sex, string][] = [['male', 'Male'], ['female', 'Female'], ['infant', 'Infant']];
  let sex = $state<Sex>('male');
  let seed = $state(1);
  let reason = $state<string | null>(null);
  let canvas = $state<HTMLCanvasElement>();
  let viewer = $state.raw<ModelViewer | null>(null);
  let shownPrefab: string | null = null;
  let wantedPrefab: string | null = null;
  let loading: Promise<void> | null = null;
  let generation = 0;
  let materialCall = 0;
  let disposed = false;
  let sexFor: string | null = null;

  $effect.pre(() => {
    // A newly picked skin opens on its own sex (Female for a female-only skin); edits to the same skin keep the choice.
    const id = skin.id;
    if (id === sexFor) return;
    sexFor = id;
    sex = untrack(() => (skin.male?.diffuse || !skin.female ? 'male' : 'female'));
  });

  /** Fetches the model and maps for the current skin and sex; a newer choice wins over a late answer. */
  async function dress() {
    const mine = ++generation;
    try {
      const model = await session.rpc.call('mods.skinModel', {
        id: doc.id, skin: skin.id, sex, ...(lod === undefined ? {} : { lod }), ...(strict ? { strict } : {}),
      });
      if (mine !== generation || disposed) return;
      const key = model.ownModel?.file ?? model.prefabRef;
      await ensureModel(key, model.ownModel ?? null);
      if (mine !== generation || disposed || !viewer || shownPrefab !== key) return;
      viewer.setAnimalMaps(Object.fromEntries(Object.entries(model.maps).map(([slot, file]) => [slot, session.platform.fileUrl(file)])));
      reason = null;
      await Promise.all([colour(mine), material(mine)]);
    } catch (e) {
      if (mine === generation) reason = asRpcError(e).message;
    }
  }

  /**
   * One model on the canvas at a time: a load already on its way for the same prefab is shared, and a load for another
   * prefab waits for it, disposing the old viewer first (two engines on one canvas share and corrupt its WebGL state).
   */
  function ensureModel(prefabRef: string, own: ModSkinOwnModel | null): Promise<void> {
    wantedPrefab = prefabRef;
    if (shownPrefab === prefabRef && !loading) return Promise.resolve();
    const before = loading;
    const mine: Promise<void> = (async () => {
      await before?.catch(() => {});
      if (shownPrefab !== prefabRef && wantedPrefab === prefabRef) await show(prefabRef, own);
    })().finally(() => {
      if (loading === mine) loading = null;
    });
    loading = mine;
    return mine;
  }

  /** prefabRef is the game prefab, or the own model's preview file (then `own` holds its file and materials). */
  async function show(prefabRef: string, own: ModSkinOwnModel | null) {
    const preview = own ? { files: [own.file], materials: own.materials } : await session.rpc.call('assets.preview', { ref: prefabRef });
    if (disposed || wantedPrefab !== prefabRef) return;
    const module = await import('$lib/assets/viewer');
    viewer?.dispose();
    viewer = null;
    shownPrefab = null;
    const next = await module.showModels(canvas!, preview.files.map((file) => ({ file, url: session.platform.fileUrl(file) })), {
      fileUrl: (path) => session.platform.fileUrl(path),
      materials: preview.materials ?? [],
    });
    if (disposed || wantedPrefab !== prefabRef) {
      next.dispose();
      return;
    }
    viewer = next;
    shownPrefab = prefabRef;
    if (!tab.active) next.pause();
  }

  async function colour(mine = generation) {
    try {
      const colors = await session.rpc.call('mods.sampleColors', { id: doc.id, skin: skin.id, colors: colorsJson, variant, seed, sex });
      if (mine === generation) viewer?.setColors(colors);
    } catch {
      // the colours could not be read (the editor shows why); the model keeps its textures
    }
  }

  /** The material's iridescence on the model; the newest call wins. */
  async function material(mine = generation) {
    const call = ++materialCall;
    try {
      const p = await session.rpc.call('mods.materialPreview', { id: doc.id, skin: skin.id, materials: materialsJson, sex });
      if (mine !== generation || call !== materialCall) return;
      const url = (file: string | null) => (file ? session.platform.fileUrl(file) : null);
      viewer?.setIridescence({ uniforms: iridescenceUniforms(p), ramp: url(p.ramp), mask: url(p.mask) });
    } catch {
      // the material could not be previewed (the Material card shows why); the model keeps what it shows
    }
  }

  // The skin, mod.json (the revision), a file it names (the files stamp: a PNG or material image re-imported under the same
  // name) the sex or the level of detail changed: dress again, which previews the material again too.
  $effect(() => {
    void skin.id;
    void sex;
    void lod;
    void doc.detail?.revision;
    void doc.detail?.filesStamp;
    void dress();
  });

  // The colours, the animal type or the seed changed: colour again, shortly after typing stops.
  $effect(() => {
    void colorsJson;
    void variant;
    void seed;
    const timer = setTimeout(() => void colour(), 250);
    return () => clearTimeout(timer);
  });

  // The material draft changed: preview it, shortly after typing stops.
  $effect(() => {
    void materialsJson;
    const timer = setTimeout(() => void material(), 250);
    return () => clearTimeout(timer);
  });

  // A hidden tab must not keep drawing.
  $effect(() => {
    if (tab.active) viewer?.resume();
    else viewer?.pause();
  });

  $effect(() => () => {
    disposed = true;
    viewer?.dispose();
  });
</script>

<div class="model3d">
  {#if reason}<p class="hint">{reason}</p>{/if}
  <canvas bind:this={canvas} class="viewport" class:hidden={!!reason} aria-label="3D preview of one animal"></canvas>
  <div class="controls">
    {#each SEXES as [value, label] (value)}
      <label><input type="radio" name="sex-{skin.id}" {value} checked={sex === value} onchange={() => (sex = value)} /> {label}</label>
    {/each}
    <button class="ghost" aria-label="Show another animal" title="Show another animal" onclick={() => (seed += 1)}>↻</button>
    <span class="note">3D look: close, not exact (lighting, rim and fur are approximated).</span>
  </div>
</div>

<style>
  .model3d { display: grid; gap: 6px; }
  .viewport { width: 100%; height: 260px; display: block; border-radius: var(--radius); background: var(--panel-2); }
  .hidden { display: none; }
  .controls { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
  .note { font-size: 12px; color: var(--muted); }
</style>
