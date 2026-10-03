<script lang="ts">
  import type { RpcError } from '$lib/rpc/client';
  import { fixLabel } from '$lib/fixes';

  let { error, onFix, onDismiss }: { error: RpcError; onFix: (fix: string) => void; onDismiss: () => void } = $props();

  const lines = $derived(error.message.split('\n'));
  const label = $derived(error.fix ? fixLabel(error.fix) : null);
</script>

<div class="banner error" role="alert">
  <div class="text">
    <strong>{lines[0]}</strong>
    {#if lines.length > 1}<pre>{lines.slice(1).join('\n')}</pre>{/if}
    <span class="code">{error.code}</span>
  </div>
  {#if error.fix && label}
    <button class="primary" onclick={() => onFix(error.fix!)}>{label}</button>
  {/if}
  <button class="ghost" aria-label="Dismiss" onclick={onDismiss}>✕</button>
</div>
