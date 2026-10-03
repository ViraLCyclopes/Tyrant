<script lang="ts">
  import '../app.css';
  import { onMount } from 'svelte';
  import AppShell from '$lib/components/AppShell.svelte';
  import { tauriPlatform } from '$lib/platform';
  import { RpcClient } from '$lib/rpc/client';
  import { TauriTransport, insideTauri } from '$lib/rpc/tauri';
  import { browserStore } from '$lib/storage';
  import { Session, setSession } from '$lib/stores/session.svelte';

  let { children } = $props();

  const session = insideTauri() ? new Session(new RpcClient(new TauriTransport()), tauriPlatform, browserStore) : null;
  if (session) setSession(session);

  onMount(() => {
    void session?.start();
  });
</script>

{#if session}
  <AppShell>{@render children()}</AppShell>
{:else}
  <main class="outside">
    <h1>Tyrant</h1>
    <p>This is Tyrant's interface. Start it with <code>npm run tauri dev</code> or the installed app.</p>
  </main>
{/if}

<style>
  .outside { padding: 40px; }
</style>
