<script lang="ts">
  import { onMount } from 'svelte';

  let {
    name,
    installed,
    error = null,
    busy = false,
    onConfirm,
    onCancel,
  }: { name: string; installed: boolean; error?: string | null; busy?: boolean; onConfirm: (removeFromGame: boolean) => void; onCancel: () => void } = $props();
  let removeFromGame = $state(true);
  let dialog = $state<HTMLElement>();

  onMount(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialog?.focus();
    return () => previous?.focus?.();
  });

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape' && !busy) onCancel();
  }
</script>

<svelte:window onkeydown={onKey} />

<div class="scrim" role="presentation" onclick={() => !busy && onCancel()}>
  <div class="dialog" role="dialog" aria-modal="true" aria-label="Delete {name}" tabindex="-1" bind:this={dialog} onclick={(e) => e.stopPropagation()} onkeydown={() => {}}>
    <h2>Delete '{name}'?</h2>
    <p>
      Its folder and its Blender projects go to the Recycle Bin, where you can restore them; Ctrl+Z cannot bring it back. Its skin numbers stay
      reserved, so saved animals keep their skins if it comes back.
    </p>
    {#if installed}<label class="row"><input type="checkbox" bind:checked={removeFromGame} /> Also remove it from the game</label>{/if}
    {#if error}<p class="warn" role="alert">{error}</p>{/if}
    <div class="actions">
      <button onclick={onCancel} disabled={busy}>Cancel</button>
      <button class="primary" onclick={() => onConfirm(installed && removeFromGame)} disabled={busy}>{busy ? 'Deleting…' : 'Delete mod'}</button>
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
