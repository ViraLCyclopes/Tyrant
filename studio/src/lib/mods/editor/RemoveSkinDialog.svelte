<script lang="ts">
  import { onMount } from 'svelte';

  let { name, onConfirm, onCancel }: { name: string; onConfirm: (deleteFiles: boolean) => void; onCancel: () => void } = $props();
  let deleteFiles = $state(false);
  let dialog = $state<HTMLElement>();

  onMount(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialog?.focus();
    return () => previous?.focus?.();
  });

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') onCancel();
  }
</script>

<svelte:window onkeydown={onKey} />

<div class="scrim" role="presentation" onclick={onCancel}>
  <div class="dialog" role="dialog" aria-modal="true" aria-label="Remove {name}" tabindex="-1" bind:this={dialog} onclick={(e) => e.stopPropagation()} onkeydown={() => {}}>
    <h2>Remove '{name}'?</h2>
    <p>Its number in the game stays reserved, so other skins keep theirs; saved animals with this skin fall back to its base skin.</p>
    <label class="row"><input type="checkbox" bind:checked={deleteFiles} /> Also delete its files (never ones another skin or replacement uses)</label>
    <div class="actions">
      <button onclick={onCancel}>Cancel</button>
      <button class="primary" onclick={() => onConfirm(deleteFiles)}>Remove skin</button>
    </div>
  </div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 50; background: #0008; display: grid; place-items: center; }
  .dialog { width: min(460px, calc(100vw - 32px)); background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; display: grid; gap: 12px; outline: none; }
  h2, p { margin: 0; }
  .row { display: flex; gap: 8px; align-items: flex-start; }
  .actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>
