<script lang="ts">
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import { restoreCutouts } from '../modActions';
  import type { ModDoc } from './modDoc.svelte';

  let { doc }: { doc: ModDoc } = $props();
  const session = getSession();
  const tab = getTab();
  const report = $derived(doc.check);
</script>

<section class="card check">
  <h2>Check</h2>
  {#if !report}
    <p class="hint">Checking…</p>
  {:else}
    {#each report.errors as line (line)}<p class="warn">{line}</p>{/each}
    {#each report.warnings as line (line)}<p class="hint">{line}</p>{/each}
    {#if !report.errors.length && !report.warnings.length}<p class="hint">No problems found.</p>{/if}
  {/if}
  <div class="row">
    <button onclick={() => doc.runCheck()}>Check again</button>
    {#if report?.missingCutouts?.length}
      <button onclick={async () => { if (await restoreCutouts(session, tab, doc.id)) await doc.reload(); }} disabled={session.busy}>Restore cutouts</button>
    {/if}
  </div>
</section>

<style>
  .check { display: grid; gap: 6px; }
  .check p { margin: 0; }
  .row { display: flex; gap: 8px; margin-top: 6px; }
</style>
