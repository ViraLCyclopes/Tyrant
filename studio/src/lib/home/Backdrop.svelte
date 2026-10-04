<script lang="ts">
  import { getTab } from '$lib/shell/tab.svelte';

  /**
   * The start screen's background: the pictures in turn, each fading into the next with a slow drift. Two layers take turns:
   * the hidden one already holds the next picture (so it is loaded before it fades in). Still while the tab is in the background.
   */
  let { images, interval = 9000 }: { images: readonly string[]; interval?: number } = $props();

  const tab = getTab();
  const fade = $derived(Math.min(1600, interval / 2));
  let index = $state(0);
  let front = $state(0);
  // svelte-ignore state_referenced_locally
  let layers = $state([images[0] ?? '', images[1] ?? images[0] ?? '']);

  $effect(() => {
    if (!tab.active || images.length < 2) return;
    const timers: ReturnType<typeof setTimeout>[] = [];
    const timer = setInterval(() => {
      index = (index + 1) % images.length;
      front = 1 - front; // the hidden layer already shows images[index]
      const behind = 1 - front;
      const next = images[(index + 1) % images.length];
      timers.push(setTimeout(() => (layers[behind] = next), fade)); // after the fade, load the one after behind it
    }, interval);
    return () => {
      clearInterval(timer);
      timers.forEach(clearTimeout);
    };
  });
</script>

<div class="backdrop" aria-hidden="true" style:--fade="{fade}ms" style:--drift="{interval + fade}ms">
  {#each layers as src, i (i)}
    <div class="layer" class:on={front === i} data-image={src} style:background-image="url('{src}')"></div>
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
