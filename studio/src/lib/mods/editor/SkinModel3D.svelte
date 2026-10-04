<script lang="ts">
  import type { ModelViewer } from '$lib/assets/viewer';
  import { asRpcError } from '$lib/rpc/client';
  import type { ModSkinDto } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { Variant } from './colors';
  import type { ModDoc } from './modDoc.svelte';

  /** One animal of the skin in 3D: the species' model wearing the skin's maps, with the 2D strip's first animal's colours. */
  let { doc, skin, colorsJson, variant }: { doc: ModDoc; skin: ModSkinDto; colorsJson: string | null; variant: Variant } = $props();
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
  let generation = 0;
  let disposed = false;

  $effect.pre(() => {
    // A skin with only female files opens on Female.
    sex = skin.male?.diffuse || !skin.female ? 'male' : 'female';
  });

  /** Fetches the model and maps for the current skin and sex; a newer choice wins over a late answer. */
  async function dress() {
    const mine = ++generation;
    try {
      const model = await session.rpc.call('mods.skinModel', { id: doc.id, skin: skin.id, sex });
      if (mine !== generation || disposed) return;
      if (shownPrefab !== model.prefabRef) await show(model.prefabRef, mine);
      if (mine !== generation || disposed || !viewer) return;
      viewer.setAnimalMaps(Object.fromEntries(Object.entries(model.maps).map(([slot, file]) => [slot, session.platform.fileUrl(file)])));
      reason = null;
      await colour(mine);
    } catch (e) {
      if (mine === generation) reason = asRpcError(e).message;
    }
  }

  async function show(prefabRef: string, mine: number) {
    const preview = await session.rpc.call('assets.preview', { ref: prefabRef });
    if (mine !== generation || disposed) return;
    const module = await import('$lib/assets/viewer');
    const next = await module.showModels(canvas!, preview.files.map((file) => ({ file, url: session.platform.fileUrl(file) })), {
      fileUrl: (path) => session.platform.fileUrl(path),
      materials: preview.materials ?? [],
    });
    if (disposed) {
      next.dispose();
      return;
    }
    viewer?.dispose();
    viewer = next;
    shownPrefab = prefabRef;
    if (!tab.active) next.pause();
  }

  async function colour(mine = generation) {
    try {
      const colors = await session.rpc.call('mods.sampleColors', { id: doc.id, skin: skin.id, colors: colorsJson, variant, seed });
      if (mine === generation) viewer?.setColors(colors);
    } catch {
      // the colours could not be read (the editor shows why); the model keeps its textures
    }
  }

  // The skin, its files (the mod's revision) or the sex changed: dress again.
  $effect(() => {
    void skin.id;
    void sex;
    void doc.detail?.revision;
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
