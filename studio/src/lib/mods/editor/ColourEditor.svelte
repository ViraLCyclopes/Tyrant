<script lang="ts">
  import type { ModSkinDto } from '$lib/rpc/types.gen';
  import type { ModDoc } from './modDoc.svelte';

  let { doc, skin }: { doc: ModDoc; skin: ModSkinDto } = $props();
</script>

<!-- Replaced by the full colour editor in the next task. -->
<p class="hint">Colour editor for {skin.name} ({doc.id}) comes in the next task.</p>
