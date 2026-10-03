<script lang="ts">
  import type { AssetPreview } from '$lib/rpc/types.gen';
  import { getSession } from '$lib/stores/session.svelte';

  type Channel = 'rgb' | 'r' | 'g' | 'b' | 'a';
  let { preview }: { preview: AssetPreview } = $props();

  const session = getSession();
  const channels: { id: Channel; label: string }[] = [
    { id: 'rgb', label: 'RGB' },
    { id: 'r', label: 'R' },
    { id: 'g', label: 'G' },
    { id: 'b', label: 'B' },
    { id: 'a', label: 'Alpha' },
  ];
  let channel = $state<Channel>('rgb');
  const src = $derived(session.platform.fileUrl(preview.files[0]));
</script>

<!-- Packed maps keep different data per channel (e.g. smoothness in alpha); each view shows one channel as grey. -->
<svg class="filters" width="0" height="0" aria-hidden="true">
  <filter id="channel-r"><feColorMatrix type="matrix" values="1 0 0 0 0  1 0 0 0 0  1 0 0 0 0  0 0 0 0 1" /></filter>
  <filter id="channel-g"><feColorMatrix type="matrix" values="0 1 0 0 0  0 1 0 0 0  0 1 0 0 0  0 0 0 0 1" /></filter>
  <filter id="channel-b"><feColorMatrix type="matrix" values="0 0 1 0 0  0 0 1 0 0  0 0 1 0 0  0 0 0 0 1" /></filter>
  <filter id="channel-a"><feColorMatrix type="matrix" values="0 0 0 1 0  0 0 0 1 0  0 0 0 1 0  0 0 0 0 1" /></filter>
</svg>

<div class="toolbar" role="group" aria-label="Channels">
  {#each channels as c (c.id)}
    <button aria-pressed={channel === c.id} onclick={() => (channel = c.id)}>{c.label}</button>
  {/each}
</div>
<div class="checker">
  <img {src} alt="Texture preview" class={channel === 'rgb' ? '' : `channel-${channel}`} />
</div>
<p class="hint">{preview.width} × {preview.height} · {preview.format} · {preview.mipCount} mip levels</p>

<style>
  .filters { position: absolute; }
  .checker {
    background: repeating-conic-gradient(#2a2a2a 0% 25%, #3a3a3a 0% 50%) 50% / 16px 16px;
    border-radius: var(--radius); padding: 8px; display: flex; justify-content: center;
  }
  img { max-width: 100%; max-height: 420px; image-rendering: auto; }
  .channel-r { filter: url(#channel-r); }
  .channel-g { filter: url(#channel-g); }
  .channel-b { filter: url(#channel-b); }
  .channel-a { filter: url(#channel-a); }
  button[aria-pressed='true'] { background: var(--accent); color: var(--accent-text); }
</style>
