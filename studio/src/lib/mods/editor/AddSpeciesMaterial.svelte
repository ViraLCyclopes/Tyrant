<script lang="ts">
  import AddPrefabTarget from './AddPrefabTarget.svelte';
  import type { ModDoc } from './modDoc.svelte';

  /** The mod editor's Species materials → + Add: pick the species (this mod's or the game's) whose material to change. */
  let { doc, onDone }: { doc: Pick<ModDoc, 'id'>; onDone: (species: string | null) => void } = $props();
</script>

<AddPrefabTarget {doc} {onDone} title="Add species materials" what="Material settings" opens="the species' material settings" />
