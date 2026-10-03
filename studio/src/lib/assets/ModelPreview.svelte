<script lang="ts">
  import { onMount } from 'svelte';
  import type { AssetPreview, EnvironmentList } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModelViewer } from './viewer';

  let { preview }: { preview: AssetPreview } = $props();

  const GROUND_KEY = 'tyrant.viewer.ground';
  const SKY_KEY = 'tyrant.viewer.sky';
  const session = getSession();
  const tab = getTab();
  let canvas = $state<HTMLCanvasElement>();
  let failed = $state<string | null>(null);
  let notes = $state<string[]>([]);
  let skeleton = $state(false);
  let textures = $state(true);
  let environments = $state<EnvironmentList | null>(null);
  let ground = $state(session.store.get(GROUND_KEY) ?? 'lush-grass');
  let sky = $state(session.store.get(SKY_KEY) ?? 'noon');
  let skin = $state('');
  let viewer = $state.raw<ModelViewer | null>(null);
  const textured = $derived((preview.materials ?? []).some((m) => m.baseColor !== null || m.normal !== null));
  const ownSkin = $derived(preview.skins?.find((s) => s.current)?.ref ?? '');

  onMount(() => {
    let disposed = false;
    skin = ownSkin;
    const models = preview.files.map((file) => ({ file, url: session.platform.fileUrl(file) }));
    const skinnable = (preview.materials ?? []).filter((m) => m.skinnable).map((m) => m.name);
    session.rpc.call('assets.environments').then(
      (list) => (environments = list),
      () => (environments = null), // the viewer works without surroundings
    );
    import('./viewer')
      .then((module) => module.showModels(canvas!, models, { fileUrl: (path) => session.platform.fileUrl(path), skinnable }))
      .then((v) => {
        if (disposed) {
          v.dispose();
          return;
        }
        viewer = v;
        void chooseGround(ground);
        void chooseSky(sky);
      })
      .catch((e: unknown) => (failed = message(e)));
    return () => {
      disposed = true;
      viewer?.dispose();
      viewer = null;
    };
  });

  // A hidden tab must not keep drawing.
  $effect(() => {
    if (tab.active) viewer?.resume();
    else viewer?.pause();
  });

  function message(e: unknown): string {
    return e instanceof Error ? e.message : String(e);
  }

  function note(text: string) {
    if (!notes.includes(text)) notes = [...notes, text];
  }

  /** The environment's files as URLs; null (with a note) when the game texture cannot be read. */
  async function environment(id: string, what: string): Promise<string[] | null> {
    try {
      const texture = await session.rpc.call('assets.environment', { id });
      return texture.files.map((file) => session.platform.fileUrl(file));
    } catch (e) {
      note(`The ${what} could not be loaded: ${message(e)}`);
      return null;
    }
  }

  // Each choice checks it is still the current one when its files arrive, so a slow answer never undoes a later choice.
  async function chooseGround(id: string) {
    ground = id;
    session.store.set(GROUND_KEY, id);
    const files = id ? await environment(id, 'ground') : null;
    if (ground === id) viewer?.setGround(files?.[0] ?? null);
  }

  async function chooseSky(id: string) {
    sky = id;
    session.store.set(SKY_KEY, id);
    const files = id ? await environment(id, 'sky') : null;
    if (sky === id) viewer?.setSky(files);
  }

  async function chooseSkin(ref: string) {
    skin = ref;
    if (ref === ownSkin) {
      viewer?.setSkin(null);
      return;
    }
    try {
      const texture = await session.rpc.call('assets.preview', { ref });
      if (skin === ref) viewer?.setSkin(session.platform.fileUrl(texture.files[0]));
    } catch (e) {
      note(`That skin could not be loaded: ${message(e)}`);
    }
  }
</script>

<canvas bind:this={canvas} class="viewport" aria-label="3D preview"></canvas>
{#if failed}<p class="warn">The 3D preview could not be shown: {failed}</p>{/if}
{#each notes as text (text)}<p class="warn">{text}</p>{/each}
<div class="row tools">
  {#if textured}
    <label>
      <input
        type="checkbox"
        checked={textures}
        onchange={(e) => {
          textures = e.currentTarget.checked;
          viewer?.setTextures(textures);
        }}
      />
      Textures
    </label>
  {/if}
  {#if preview.skins?.length}
    <label>
      Skin
      <select aria-label="Skin" value={skin} onchange={(e) => chooseSkin(e.currentTarget.value)}>
        {#each preview.skins as option (option.ref)}
          <option value={option.ref}>{option.name}</option>
        {/each}
      </select>
    </label>
  {/if}
  {#if environments}
    <label>
      Ground
      <select aria-label="Ground" value={ground} onchange={(e) => chooseGround(e.currentTarget.value)}>
        <option value="">None</option>
        {#each environments.grounds as option (option.id)}
          <option value={option.id}>{option.label}</option>
        {/each}
      </select>
    </label>
    <label>
      Sky
      <select aria-label="Sky" value={sky} onchange={(e) => chooseSky(e.currentTarget.value)}>
        <option value="">None</option>
        {#each environments.skies as option (option.id)}
          <option value={option.id}>{option.label}</option>
        {/each}
      </select>
    </label>
  {/if}
  {#if preview.skinned}
    <label>
      <input
        type="checkbox"
        checked={skeleton}
        onchange={(e) => {
          skeleton = e.currentTarget.checked;
          viewer?.setSkeleton(skeleton);
        }}
      />
      Show skeleton
    </label>
  {/if}
  <button onclick={() => viewer?.frame()}>Frame</button>
</div>
<p class="hint">{preview.vertices?.toLocaleString('en-US')} vertices · {preview.triangles?.toLocaleString('en-US')} triangles · {preview.files.length} part(s)</p>
<p class="hint">
  Middle- or left-drag orbits · Shift-drag pans · wheel or Ctrl-drag zooms · numpad 1/3/7 front, side, top (Ctrl: opposite) ·
  5 perspective/orthographic · . or Home frames.{#if preview.skins?.length} In game, genetics tint the skin; the preview shows it untinted.{/if}
</p>

<style>
  .viewport { width: 100%; height: 360px; display: block; border-radius: var(--radius); border: 1px solid var(--border); }
  .tools { flex-wrap: wrap; }
</style>
