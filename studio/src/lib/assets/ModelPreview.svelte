<script lang="ts">
  import { onMount } from 'svelte';
  import type { AssetPreview } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ModelViewer } from './viewer';

  let { preview }: { preview: AssetPreview } = $props();

  const session = getSession();
  let canvas = $state<HTMLCanvasElement>();
  let failed = $state<string | null>(null);
  let skeleton = $state(false);
  let viewer: ModelViewer | null = null;

  onMount(() => {
    let disposed = false;
    const urls = preview.files.map((file) => session.platform.fileUrl(file));
    import('./viewer')
      .then((module) => module.showModels(canvas!, urls))
      .then((v) => {
        if (disposed) v.dispose();
        else viewer = v;
      })
      .catch((e: unknown) => (failed = e instanceof Error ? e.message : String(e)));
    return () => {
      disposed = true;
      viewer?.dispose();
    };
  });
</script>

<canvas bind:this={canvas} class="viewport" aria-label="3D preview"></canvas>
{#if failed}<p class="warn">The 3D preview could not be shown: {failed}</p>{/if}
<div class="row">
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
  <span class="hint">
    {preview.vertices?.toLocaleString('en-US')} vertices · {preview.triangles?.toLocaleString('en-US')} triangles · {preview.files.length} part(s)
  </span>
</div>

<style>
  .viewport { width: 100%; height: 360px; display: block; border-radius: var(--radius); border: 1px solid var(--border); }
</style>
