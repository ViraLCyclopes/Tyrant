<script lang="ts">
  import ModelRigConfirm from './ModelRigConfirm.svelte';
  import { pendingRig } from './modelRig';

  /** Where a card shows the rig-edit question its Add / Replace / Re-import is waiting on. */
</script>

{#if $pendingRig}
  {@const pending = $pendingRig}
  <ModelRigConfirm preview={pending.preview} action={pending.action} onConfirm={(rig) => pending.resolve(rig)} onCancel={() => pending.resolve(null)} />
{/if}
