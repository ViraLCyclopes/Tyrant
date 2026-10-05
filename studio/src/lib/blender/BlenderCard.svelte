<script lang="ts">
  import { onMount } from 'svelte';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import { getBlender } from './blender.svelte';

  /** The Workspace tab's Blender card: which Blender Tyrant uses, and Tyrant's add-on in it. */
  const session = getSession();
  const tab = getTab();
  const blender = getBlender(session);
  const status = $derived(blender.status);

  onMount(() => void blender.ensure(tab, true));

  async function choose() {
    const exe = await session.platform.openFile('Choose blender.exe', ['exe']);
    if (exe) await blender.setPath(tab, exe);
  }
</script>

<section class="card" aria-labelledby="blender-heading">
  <h2 id="blender-heading">Blender</h2>
  {#if status}
    {#if status.found}
      <p><strong>Blender {status.version}</strong> · <span class="hint">{status.exe}</span></p>
    {/if}
    {#if status.problem}<p class="warn">{status.problem}</p>{/if}
    {#if status.found && status.supported}
      {#if status.addon === 'missing'}
        <p>Tyrant's add-on is not installed in this Blender (Tyrant has {status.addonBundled}).</p>
      {:else if status.addon === 'older'}
        <p class="warn">Tyrant's add-on in Blender is older ({status.addonInstalled}) than this Tyrant's ({status.addonBundled}).</p>
      {:else if status.addon === 'changed'}
        <p class="warn">Blender has an older build of Tyrant's add-on {status.addonInstalled}: update it to this Tyrant's.</p>
      {:else}
        <p>Add-on {status.addonInstalled} installed.</p>
      {/if}
    {/if}
    {#if blender.installing}
      <div class="installing">
        <progress aria-label="Installing the add-on"></progress>
        <span>Installing Tyrant's add-on into Blender {status.version}… (a few seconds)</span>
      </div>
    {:else if blender.installedNote}
      <p class="done" role="status">{blender.installedNote}</p>
    {/if}
    <p class="hint">Open in Blender (species, skins, models) opens the game's model in Blender 5 looking like the game; the Tyrant panel in Blender (N → Tyrant) sends your edit back into the mod.</p>
    <div class="row">
      {#if blender.installing}
        <button class="primary" disabled>Installing…</button>
      {:else if status.found && status.supported && blender.needsInstall()}
        <button class="primary" onclick={() => blender.install(tab)} disabled={session.busy}>{status.addon === 'missing' ? 'Install add-on' : 'Update add-on'}</button>
      {:else if status.found && status.supported}
        <!-- Same files: put this Tyrant's copy in again anyway (e.g. after editing it by hand). -->
        <button onclick={() => blender.install(tab)} disabled={session.busy}>Reinstall add-on</button>
      {/if}
      <button onclick={choose} disabled={session.busy}>Choose blender.exe…</button>
      {#if status.found || status.missingConfigured}
        <button onclick={() => blender.setPath(tab, null)} disabled={session.busy}>Find automatically</button>
      {/if}
    </div>
  {:else}
    <p class="hint">Looking for Blender…</p>
  {/if}
</section>

<style>
  .installing { display: flex; align-items: center; gap: 10px; margin: 8px 0; }
  .installing progress { width: 160px; }
  .done { color: var(--ok-text, var(--text)); }
</style>
