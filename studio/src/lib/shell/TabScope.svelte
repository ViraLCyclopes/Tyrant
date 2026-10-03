<script lang="ts">
  import { untrack } from 'svelte';
  import ErrorBanner from '$lib/components/ErrorBanner.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import type { ToolDef } from './registry';
  import { setTab, type Tab } from './tab.svelte';

  let { tab, def }: { tab: Tab; def: ToolDef } = $props();
  const session = getSession();
  // A TabScope lives exactly as long as its tab, so the tab and the tool's code never change underneath it.
  setTab(untrack(() => tab));
  const loading = untrack(() => def.load!());
</script>

<div class="scope" class:bare={def.id === 'home'} hidden={!tab.active}>
  {#if tab.error}
    <ErrorBanner error={tab.error} onFix={(fix) => session.applyFix(fix, tab)} onDismiss={() => (tab.error = null)} />
  {/if}
  {#await loading}
    <p class="hint">Loading {def.name}…</p>
  {:then mod}
    <mod.default />
  {:catch e}
    <p class="warn">{def.name} could not be loaded: {String(e)}</p>
  {/await}
</div>

<style>
  .scope { position: absolute; inset: 0; overflow: auto; padding: 20px 24px; }
  .scope.bare { padding: 0; overflow: hidden; }
  .scope[hidden] { display: none; }
</style>
