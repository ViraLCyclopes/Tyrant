<script lang="ts">
  import { onMount } from 'svelte';
  import type { ModelViewer } from '$lib/assets/viewer';
  import { asRpcError } from '$lib/rpc/client';
  import type { ModPortraitDto, ModSkinDto, ModSkinModel } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModDoc } from './modDoc.svelte';
  import { RENDER_SLOT, renderTargets, type RenderSex } from './portraits';

  /**
   * Render from 3D: the skin's (or species') model framed like the game's portraits; the user may orbit and zoom, then
   * Use this view renders the Normal column (adult male, adult female, infant) from that one view and saves it in one edit.
   * Hand-picked pictures are kept unless Replace my pictures is ticked.
   */
  type Target = { skin: ModSkinDto } | { speciesId: string; speciesKey: string };
  let { doc, target, shown, onclose }: { doc: ModDoc; target: Target; shown: readonly ModPortraitDto[]; onclose: () => void } = $props();
  const session = getSession();
  const tab = getTab();

  /** The rows the game shows: a male-only skin has no female row (and the other way round); the infant always. */
  const sexes = $derived<RenderSex[]>(
    'skin' in target
      ? (['male', 'female', 'infant'] as RenderSex[]).filter((s) => s === 'infant' || (s === 'male' ? !!target.skin.male : !!target.skin.female))
      : ['male', 'female', 'infant'],
  );

  let replace = $state(false);
  let status = $state<string | null>('Loading the model…');
  let busy = $state(false);
  let canvas = $state<HTMLCanvasElement>();
  let dialog = $state<HTMLElement>();
  let viewer = $state.raw<ModelViewer | null>(null);
  /** The viewer is framed and dressed: Use this view may render. */
  let ready = $state(false);
  /** The first row's maps, put back after a render so the preview shows what it showed before. */
  let firstMaps: Record<string, string> | null = null;
  let disposed = false;
  /** A click closes the dialog only when it also started on the scrim (an orbit drag released outside the dialog must not). */
  let downOnScrim = false;

  function model(sex: RenderSex): Promise<ModSkinModel> {
    return 'skin' in target
      ? session.rpc.call('mods.skinModel', { id: doc.id, skin: target.skin.id, sex })
      : session.rpc.call('mods.speciesModel', { id: doc.id, speciesId: target.speciesId, sex });
  }

  const mapUrls = (maps: Record<string, string>) =>
    Object.fromEntries(Object.entries(maps).map(([slot, file]) => [slot, session.platform.fileUrl(file)]));

  /** The model for the first row shown, with its maps and (skins) the skin's saved colours, framed as a portrait. */
  async function open() {
    try {
      const first = await model(sexes[0]);
      if (disposed) return;
      const own = first.ownModel ?? null;
      const preview = own ? { files: [own.file], materials: own.materials } : await session.rpc.call('assets.preview', { ref: first.prefabRef });
      if (disposed) return;
      const module = await import('$lib/assets/viewer');
      const next = await module.showModels(canvas!, preview.files.map((file) => ({ file, url: session.platform.fileUrl(file) })), {
        fileUrl: (path) => session.platform.fileUrl(path),
        materials: preview.materials ?? [],
      });
      if (disposed) {
        next.dispose();
        return;
      }
      viewer = next; // from here the unmount cleanup (or the catch below) disposes it
      firstMaps = mapUrls(first.maps);
      next.setAnimalMaps(firstMaps);
      if ('skin' in target) await colour(next, target.skin);
      if (disposed) return;
      next.portraitView();
      if (!tab.active) next.pause();
      ready = true;
      status = null;
    } catch (e) {
      if (disposed) return;
      status = asRpcError(e).message;
      viewer?.dispose();
      viewer = null;
    }
  }

  async function colour(next: ModelViewer, skin: ModSkinDto) {
    try {
      next.setColors(await session.rpc.call('mods.sampleColors', { id: doc.id, skin: skin.id, colors: skin.colorsJson, variant: 'normal', seed: 0 }));
    } catch {
      // the colours could not be read (the colour editor shows why); the model keeps its textures
    }
  }

  /** What went wrong with a save: doc.edit logs it to this tab rather than throwing. */
  function loggedSince(seq: number): string | null {
    return session.log.records.findLast((r) => r.seq > seq && r.tab === tab.id)?.message ?? null;
  }

  const allOwn = $derived(
    `${sexes.length === 3 ? 'All three are your own pictures' : sexes.length === 2 ? 'Both are your own pictures' : 'It is your own picture'}; ` +
      `tick Replace my pictures to render over ${sexes.length === 1 ? 'it' : 'them'}.`,
  );

  async function useView() {
    const v = viewer;
    if (!v || !ready || busy) return;
    const wanted = renderTargets(shown, sexes, replace);
    if (!wanted.length) {
      status = allOwn;
      return;
    }
    busy = true;
    status = 'Rendering…';
    try {
      const set: Record<string, { data: string; rendered: true }> = {};
      try {
        for (const sex of wanted) {
          const m = await model(sex);
          if (disposed) return;
          v.setAnimalMaps(mapUrls(m.maps));
          v.setInfant(sex === 'infant');
          await v.whenReady(); // the new maps must be loaded before the shot
          set[RENDER_SLOT[sex]] = { data: await v.renderPortrait(256, 128), rendered: true }; // the camera stays: one view for all
        }
      } finally {
        if (!disposed) {
          v.setInfant(false);
          if (firstMaps) v.setAnimalMaps(firstMaps); // not the infant's (or the last row's) textures on the shown animal
        }
      }
      if (disposed) return;
      const since = session.log.lastSeq();
      const saved =
        'skin' in target
          ? await doc.edit('mods.setPortraits', { skin: target.skin.id, set })
          : await doc.edit('mods.setSpeciesPortraits', { speciesId: target.speciesId, set }, { undoable: false });
      if (!saved) {
        status = loggedSince(since) ?? 'The portraits could not be saved.';
        return;
      }
      onclose();
    } catch (e) {
      status = asRpcError(e).message;
    } finally {
      busy = false;
    }
  }

  onMount(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialog?.focus();
    void open();
    return () => {
      disposed = true;
      viewer?.dispose();
      previous?.focus?.();
    };
  });

  // A hidden tab must not keep drawing.
  $effect(() => {
    if (tab.active) viewer?.resume();
    else viewer?.pause();
  });

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') onclose();
  }
</script>

<svelte:window onkeydown={onKey} />

<div
  class="scrim"
  role="presentation"
  onpointerdown={(e) => (downOnScrim = e.target === e.currentTarget)}
  onclick={(e) => {
    const close = downOnScrim && e.target === e.currentTarget;
    downOnScrim = false;
    if (close) onclose();
  }}
>
  <div class="dialog" role="dialog" aria-modal="true" aria-label="Render portraits from 3D" tabindex="-1" bind:this={dialog} onclick={(e) => e.stopPropagation()} onkeydown={() => {}}>
    <h2>Render portraits from 3D</h2>
    <p class="hint">Turn and zoom the animal until it looks like the game's portraits, then use this view for the Normal column.</p>
    <canvas bind:this={canvas} class="viewport" aria-label="Portrait framing"></canvas>
    <label class="row"><input type="checkbox" bind:checked={replace} onchange={() => { if (status === allOwn) status = null; }} /> Replace my pictures</label>
    <p class="status" role="status">{status ?? ''}</p>
    <div class="actions">
      <button onclick={onclose}>Cancel</button>
      <button class="primary" disabled={!ready || busy} onclick={useView}>Use this view</button>
    </div>
  </div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 50; background: #0008; display: grid; place-items: center; }
  .dialog { width: min(560px, calc(100vw - 32px)); background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; display: grid; gap: 12px; outline: none; }
  h2, p { margin: 0; }
  .viewport { width: 512px; max-width: 100%; aspect-ratio: 2 / 1; display: block; border-radius: var(--radius); background: var(--panel-2); }
  .row { display: flex; gap: 8px; align-items: center; }
  .status { min-height: 1.4em; font-size: 12px; color: var(--muted); }
  .actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>
