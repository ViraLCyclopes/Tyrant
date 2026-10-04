<script lang="ts">
  import type { ModSkinDto } from '$lib/rpc/types.gen';
  import type { ModDoc } from './modDoc.svelte';

  let { doc, skin }: { doc: ModDoc; skin: ModSkinDto } = $props();
</script>

<!-- Replaced by the full skin page in the next task. -->
<h2>{skin.name}</h2>
<p class="hint">{doc.id}</p>
