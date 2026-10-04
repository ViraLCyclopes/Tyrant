<script lang="ts">
  import { RELEASES, type Updates } from './updates.svelte';

  /** Tyrant's own update: Update now (GitHub), the Nexus page, Later; or a note from Help → Check for updates. */
  let { updates }: { updates: Updates } = $props();
  const platform = $derived(updates.platform);
</script>

{#if updates.offer}
  <div class="banner" role="status">
    {#if updates.offer.kind === 'github'}
      <strong>Tyrant {updates.offer.version} is available</strong>
      {#if updates.offer.notes}<details><summary>Notes</summary><p class="notes">{updates.offer.notes}</p></details>{/if}
      {#if updates.installing}
        <span>Installing…{updates.progress !== null ? ` ${Math.round(updates.progress * 100)}%` : ''}</span>
      {:else}
        <button class="primary" onclick={() => void updates.install()}>Update now</button>
      {/if}
      {@const page = `${RELEASES}/tag/v${updates.offer.version}`}
      <button class="ghost" onclick={() => void platform.openUrl(page)}>What's new</button>
      {#if updates.offer.nexusUrl}
        {@const url = updates.offer.nexusUrl}
        <button class="ghost" onclick={() => void platform.openUrl(url)}>Also on Nexus</button>
      {/if}
    {:else}
      {@const url = updates.offer.url}
      <strong>Tyrant {updates.offer.version} is on Nexus</strong>
      <button class="primary" onclick={() => void platform.openUrl(url)}>Open Nexus page</button>
    {/if}
    <button class="ghost" onclick={() => updates.dismiss()}>Later</button>
    {#if updates.note}<span class="note">{updates.note}</span>{/if}
  </div>
{:else if updates.note}
  <div class="banner" role="status">
    <span class="note">{updates.note}</span>
    <button class="ghost" onclick={() => updates.dismiss()}>OK</button>
  </div>
{/if}

<style>
  .banner { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; padding: 6px 12px; background: var(--panel-2); border-bottom: 1px solid var(--border); }
  .notes { white-space: pre-wrap; margin: 4px 0 0; }
  details { flex-basis: 100%; order: 10; }
</style>
