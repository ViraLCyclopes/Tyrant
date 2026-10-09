<script lang="ts">
  import { onMount } from 'svelte';
  import { MAX_GROUP } from './skinGroups';

  let {
    title,
    label,
    action,
    initial = '',
    onConfirm,
    onCancel,
  }: { title: string; label: string; action: string; initial?: string; onConfirm: (name: string) => void; onCancel: () => void } = $props();
  let name = $state('');
  let input = $state<HTMLInputElement>();
  const ok = $derived(name.trim().length > 0);

  onMount(() => {
    name = initial;
    input?.focus();
    input?.select();
  });

  function submit(e: SubmitEvent) {
    e.preventDefault();
    if (ok) onConfirm(name.trim());
  }

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') onCancel();
  }
</script>

<svelte:window onkeydown={onKey} />

<div class="scrim" role="presentation" onclick={onCancel}>
  <div class="dialog" role="dialog" aria-modal="true" aria-label={title} tabindex="-1" onclick={(e) => e.stopPropagation()} onkeydown={() => {}}>
    <form onsubmit={submit}>
      <h2>{title}</h2>
      <label class="field">{label} <input aria-label={label} bind:this={input} bind:value={name} maxlength={MAX_GROUP} /></label>
      <div class="actions">
        <button type="button" onclick={onCancel}>Cancel</button>
        <button class="primary" type="submit" disabled={!ok}>{action}</button>
      </div>
    </form>
  </div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 50; background: #0008; display: grid; place-items: center; }
  .dialog { width: min(400px, calc(100vw - 32px)); background: var(--panel); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; outline: none; }
  form { display: grid; gap: 12px; }
  h2 { margin: 0; }
  .field { display: grid; gap: 4px; }
  .actions { display: flex; justify-content: flex-end; gap: 8px; }
</style>
