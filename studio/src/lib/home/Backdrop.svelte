<script lang="ts">
  import { getTab } from '$lib/shell/tab.svelte';
  import type { Slideshow } from './slideshow.svelte';

  /** The start screen's background: the slideshow's pictures, each fading into the next with a slow drift. Still while the tab is in the background. */
  let { slideshow, interval = 9000 }: { slideshow: Slideshow; interval?: number } = $props();

  const tab = getTab();

  $effect(() => {
    void slideshow.restarts; // a dot was clicked: start the timer over
    if (!tab.active || slideshow.images.length < 2) return;
    const timer = setInterval(() => slideshow.next(), interval);
    return () => clearInterval(timer);
  });
  $effect(() => () => slideshow.dispose());
</script>

<div class="backdrop" aria-hidden="true" style:--fade="{slideshow.fade}ms" style:--drift="{interval + slideshow.fade}ms">
  {#each slideshow.layers as src, i (i)}
    <div class="layer" class:on={slideshow.front === i} data-image={src} style:background-image="url('{src}')"></div>
  {/each}
</div>

<style>
  .backdrop { position: absolute; inset: 0; background: var(--bg); overflow: hidden; }
  .layer {
    position: absolute;
    inset: 0;
    background-position: center;
    background-size: cover;
    background-repeat: no-repeat;
    opacity: 0;
    transition: opacity var(--fade) ease-in-out;
  }
  .layer.on { opacity: 1; animation: drift var(--drift) linear forwards; }
  @keyframes drift {
    from { transform: scale(1); }
    to { transform: scale(1.06); }
  }
  @media (prefers-reduced-motion: reduce) {
    .layer.on { animation: none; }
  }
</style>
