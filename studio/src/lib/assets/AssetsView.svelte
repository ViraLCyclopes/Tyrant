<script lang="ts">
  import { getSession } from '$lib/stores/session.svelte';
  import AssetBrowser from './AssetBrowser.svelte';

  const session = getSession();
</script>

<h1>Assets</h1>

{#if !session.workspace}
  <p class="empty">Open a workspace on the Home tab first.</p>
{:else if !session.workspace.hasAssetIndex}
  <p class="empty">The game's assets are not indexed yet. Click <strong>Index assets</strong> on the Home tab (about 20 seconds).</p>
{:else}
  <AssetBrowser />
{/if}
