<script lang="ts">
  import { onDestroy } from 'svelte';
  import ModelRigConfirm from './ModelRigConfirm.svelte';
  import type { RigAsk } from './modelRig';

  /** Where a card shows the rig-edit question its Add / Replace / Re-import is waiting on; leaving the card answers it with Cancel. */
  let { ask }: { ask: RigAsk } = $props();
  // svelte-ignore state_referenced_locally
  const pending = ask.pending;
  onDestroy(() => ask.dispose());
</script>

{#if $pending}
  {@const p = $pending}
  <ModelRigConfirm preview={p.preview} action={p.action} onConfirm={(rig) => p.resolve(rig)} onCancel={() => p.resolve(null)} />
{/if}
