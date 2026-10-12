<script lang="ts">
  import { onMount } from 'svelte';

  /** Export for sharing: whether the zip also carries the PNGs its cooked pack holds (off: smaller, like an install). */
  let { name, onexport, oncancel }: { name: string; onexport: (includePngs: boolean) => void; oncancel: () => void } = $props();
  let includePngs = $state(false);
  let dialog = $state<HTMLElement>();
  /** A click closes the dialog only when it also started on the scrim. */
  let downOnScrim = false;

  onMount(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialog?.focus();
    return () => previous?.focus?.();
  });

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') oncancel();
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
    if (close) oncancel();
  }}
>
  <div class="dialog" role="dialog" aria-modal="true" aria-label="Export {name} for sharing" tabindex="-1" bind:this={dialog} onclick={(e) => e.stopPropagation()} onkeydown={() => {}}>
    <h2>Export '{name}' for sharing</h2>
    <p class="hint">The zip holds the cooked textures (textures.tpack). Players unzip it into the game folder.</p>
    <label class="row"><input type="checkbox" bind:checked={includePngs} /> Include the original PNGs (bigger; lets others edit them without quality loss)</label>
    <div class="actions">
      <button onclick={oncancel}>Cancel</button>
      <button class="primary" onclick={() => onexport(includePngs)}>Export…</button>
    </div>
  </div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 50; background: #0008; display: grid; place-items: center; }
  .dialog { width: min(520px, calc(100vw - 32px)); background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; display: grid; gap: 12px; outline: none; }
  h2, p { margin: 0; }
  .row { display: flex; gap: 8px; align-items: center; }
  .actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>
