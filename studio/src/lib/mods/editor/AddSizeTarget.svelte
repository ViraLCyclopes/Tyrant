<script lang="ts">
  import AddPrefabTarget from './AddPrefabTarget.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /** The mod editor's Sizes → + Add: pick the species (this mod's or the game's) whose size to change. */
  let { doc, onDone }: { doc: Pick<ModDoc, 'id'>; onDone: (target: string | null) => void } = $props();
</script>

<AddPrefabTarget {doc} {onDone} title="Add sizes" what="Sizes" opens="the animal's size settings" />
