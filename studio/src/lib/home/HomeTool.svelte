<script lang="ts">
  import { getShell } from '$lib/shell/shellState.svelte';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import Backdrop from './Backdrop.svelte';
  import { BACKGROUNDS, rotation } from './backgrounds';
  import { Slideshow } from './slideshow.svelte';
  import Dock from './Dock.svelte';

  const session = getSession();
  const tab = getTab();
  const shell = getShell();
  const slideshow = new Slideshow(rotation(BACKGROUNDS, Math.random), 1600);
  const firstRun = $derived(session.workspace === null);
  const showIntro = $derived(firstRun || !shell.introHidden);

  const chip = $derived.by(() => {
    const ws = session.workspace;
    if (!ws) return null;
    const name = ws.dir.split(/[\\/]/).filter(Boolean).at(-1) ?? ws.dir;
    const how = ws.stale ? 'outputs stale' : ws.outputs.length === 0 ? 'no data yet' : 'up to date';
    return `Workspace: ${name} · ${how}`;
  });

  async function newWorkspace() {
    const dir = await session.platform.pickFolder('Choose an empty folder for the new workspace');
    if (dir) await session.createWorkspace(dir);
  }

  async function openWorkspace() {
    const dir = await session.platform.pickFolder('Open a workspace folder');
    if (dir) await session.openWorkspace(dir);
  }

  async function pickGame() {
    const dir = await session.platform.pickFolder('Select the Prehistoric Kingdom folder');
    if (dir) await session.detectGame(dir);
  }
</script>

<div class="home">
  <Backdrop {slideshow} />
  <div class="scrim" aria-hidden="true"></div>
  {#if showIntro}
    <section class="intro" aria-label="About Tyrant">
      <h1>Tyrant</h1>
      <p>A modding toolset for Prehistoric Kingdom: decompile the code, browse and export the game's assets and data, and make mods — new skins, colours and texture replacements.</p>
      <p class="honest">Tyrant never replaces game files: mods are loaded by its own framework and can be turned off or removed.</p>
      {#if firstRun}
        {#if !session.install}
          <p class="hint">Prehistoric Kingdom was not found automatically.</p>
          <div class="row"><button onclick={pickGame} disabled={session.busy}>Find the game folder…</button></div>
        {/if}
        <div class="row">
          <button class="primary" onclick={newWorkspace} disabled={session.busy || !session.install}>New workspace…</button>
          <button onclick={openWorkspace} disabled={session.busy}>Open workspace…</button>
        </div>
      {:else}
        <div class="row"><button class="ghost" onclick={() => shell.setIntroHidden(true)}>✕ Hide this</button></div>
      {/if}
    </section>
  {:else}
    <button class="info" aria-label="About Tyrant" title="About Tyrant" onclick={() => shell.setIntroHidden(false)}>i</button>
  {/if}
  {#if chip}
    <button class="chip" onclick={() => tab.openTool('workspace')}>{chip}</button>
  {/if}
  <div class="bottom">
    {#if slideshow.images.length > 1}
      <div class="dots" role="group" aria-label="Background pictures">
        {#each slideshow.images as _, i (i)}
          <button class="dot" class:on={slideshow.index === i} aria-label="Picture {i + 1} of {slideshow.images.length}"
            aria-current={slideshow.index === i ? 'true' : undefined} onclick={() => slideshow.show(i)}></button>
        {/each}
      </div>
    {/if}
    <Dock tools={shell.registry.dock()} onOpen={(id) => tab.openTool(id)} />
  </div>
</div>

<style>
  .home { position: absolute; inset: 0; overflow: hidden; }
  .scrim { position: absolute; inset: 0; background: linear-gradient(180deg, transparent 45%, color-mix(in srgb, var(--bg) 80%, transparent)); }
  .intro { position: absolute; left: 32px; top: 32px; max-width: 460px; background: color-mix(in srgb, var(--panel) 90%, transparent); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; display: grid; gap: 10px; }
  .intro h1 { margin: 0; color: var(--accent); }
  .intro p { margin: 0; }
  .honest { color: var(--muted); }
  .row { display: flex; gap: 8px; flex-wrap: wrap; }
  .info { position: absolute; left: 24px; top: 24px; width: 30px; height: 30px; border-radius: 50%; padding: 0; font-weight: 700; }
  .chip { position: absolute; right: 24px; top: 24px; background: color-mix(in srgb, var(--panel) 90%, transparent); }
  .bottom { position: absolute; left: 50%; bottom: 24px; transform: translateX(-50%); display: flex; flex-direction: column; align-items: center; gap: 10px; max-width: calc(100% - 32px); }
  .dots { display: flex; gap: 8px; padding: 6px 10px; border-radius: 999px; background: color-mix(in srgb, var(--panel) 55%, transparent); backdrop-filter: blur(4px); }
  .dot { width: 10px; height: 10px; min-width: 0; padding: 0; border-radius: 50%; border: 1px solid color-mix(in srgb, var(--text) 70%, transparent); background: transparent; cursor: pointer; }
  .dot:hover { background: color-mix(in srgb, var(--text) 40%, transparent); }
  .dot.on { background: var(--accent); border-color: var(--accent); }
  @media (max-width: 640px) {
    .intro { left: 16px; right: 16px; top: 16px; max-width: none; }
    .chip { right: 16px; top: auto; bottom: 150px; }
  }
</style>
