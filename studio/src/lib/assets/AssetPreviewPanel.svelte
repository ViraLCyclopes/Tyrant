<script lang="ts">
  import type { AssetPreview, AssetRow } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';
  import ModelPreview from './ModelPreview.svelte';
  import TexturePreview from './TexturePreview.svelte';

  let { asset }: { asset: AssetRow } = $props();

  const session = getSession();
  let preview = $state<AssetPreview | null>(null);
  let loading = $state(false);
  let sequence = 0;
  const visual = $derived(asset.type === 'Texture2D' || asset.type === 'Mesh' || asset.type === 'GameObject');

  $effect(() => {
    const target = asset;
    preview = null;
    loading = false;
    sequence++; // drop answers for the previous asset
    if (target.type === 'Texture2D') void load(target.ref);
  });

  async function load(ref: string) {
    const mine = ++sequence;
    loading = true;
    const r = await session.quietly(() => session.rpc.call('assets.preview', { ref }));
    if (mine !== sequence) return;
    loading = false;
    preview = r;
  }
</script>

{#if visual}
  <section class="preview" aria-label="Preview">
    <h3>Preview</h3>
    {#if preview?.kind === 'texture'}
      <TexturePreview {preview} />
    {:else if preview?.kind === 'model'}
      <ModelPreview {preview} />
    {:else if loading}
      <p class="hint">Preparing the preview…</p>
    {:else if asset.type !== 'Texture2D'}
      <button onclick={() => load(asset.ref)}>Load 3D preview</button>
    {/if}
    {#if preview?.message}<p class="hint">{preview.message}</p>{/if}
  </section>
{/if}
