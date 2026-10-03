<script lang="ts">
  import { getSession } from '$lib/stores/session.svelte';
  import { dumperLabel, formatDate, outputLabel, summarizeDecompile, summarizeDump, summarizeIndex, summarizeRefresh } from '$lib/format';

  const session = getSession();

  async function report<T>(work: Promise<T | null>, summary: (result: T) => string) {
    const result = await work;
    if (result !== null) session.notice = summary(result);
  }

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
    if (!dir) return;
    if (session.workspace) await session.openWorkspace(session.workspace.dir, dir);
    else await session.detectGame(dir);
  }
</script>

<h1>Home</h1>

<section class="card" aria-labelledby="game-heading">
  <h2 id="game-heading">Game</h2>
  {#if session.install}
    <dl>
      <dt>Folder</dt><dd class="path">{session.install.rootDir}</dd>
      <dt>Build</dt><dd><code>{session.install.buildGuid}</code></dd>
      <dt>Steam app</dt><dd>{session.install.steamAppId ?? 'unknown'}</dd>
    </dl>
  {:else}
    <p class="hint">Prehistoric Kingdom was not found automatically. Pick its folder (the one with "Prehistoric Kingdom.exe").</p>
  {/if}
  <div class="row">
    <button onclick={pickGame} disabled={session.busy}>Change game folder…</button>
  </div>
</section>

<section class="card" aria-labelledby="workspace-heading">
  <h2 id="workspace-heading">Workspace</h2>
  {#if session.workspace}
    <p class="path">{session.workspace.dir}</p>
    <div class="row">
      <button onclick={() => session.platform.reveal(session.workspace!.dir)}>Show in Explorer</button>
      <button onclick={openWorkspace} disabled={session.busy}>Open another…</button>
      <button onclick={newWorkspace} disabled={session.busy}>New workspace…</button>
    </div>
  {:else}
    <p class="hint">A workspace is a folder where Tyrant keeps decompiled code, game data and exports. It is never inside the game folder.</p>
    <div class="row">
      <button class="primary" onclick={newWorkspace} disabled={session.busy || !session.install}>New workspace…</button>
      <button onclick={openWorkspace} disabled={session.busy}>Open workspace…</button>
    </div>
    {#if session.recent.length}
      <h3>Recent</h3>
      <ul class="recent">
        {#each session.recent as dir (dir)}
          <li><button class="link" onclick={() => session.openWorkspace(dir)} disabled={session.busy}>{dir}</button></li>
        {/each}
      </ul>
    {/if}
  {/if}
</section>

{#if session.workspace}
  {@const ws = session.workspace}
  <section class="card" aria-labelledby="outputs-heading">
    <h2 id="outputs-heading">Outputs</h2>
    {#if ws.stale}
      <p class="warn">The game was updated since some outputs were made. Refresh them so they match the current build.</p>
    {/if}
    <table class="grid outputs">
      <thead><tr><th>Output</th><th>Made</th><th>Status</th></tr></thead>
      <tbody>
        {#each ws.outputs as output (output.name)}
          <tr>
            <td>{outputLabel(output.name)}</td>
            <td>{formatDate(output.createdUtc)}</td>
            <td>{#if output.stale}<span class="badge warn">stale</span>{:else}<span class="badge ok">current</span>{/if}</td>
          </tr>
        {:else}
          <tr><td colspan="3" class="hint">Nothing yet. Refresh all decompiles the code, indexes the assets and dumps the game data.</td></tr>
        {/each}
      </tbody>
    </table>
    <div class="row">
      <button class="primary" onclick={() => report(session.refreshAll(), summarizeRefresh)} disabled={session.busy}>Refresh all</button>
      <button onclick={() => report(session.runJob('decompile.run', {}, 'Decompile'), summarizeDecompile)} disabled={session.busy}>Decompile code</button>
      <button onclick={() => report(session.runJob('assets.index', undefined, 'Asset index'), summarizeIndex)} disabled={session.busy}>Index assets</button>
    </div>
  </section>

  <section class="card" aria-labelledby="dumper-heading">
    <h2 id="dumper-heading">Game data dumper</h2>
    <p>Status: <strong>{dumperLabel(ws.dumper)}</strong></p>
    <p class="hint">The dumper is a small MelonLoader mod. It does nothing during normal play and only runs when you ask for a data dump.</p>
    {#if ws.dumper === 'conflict'}
      <p class="warn">Another mod loader (BepInEx) or an unknown version.dll is in the game folder. Remove it to use the dumper.</p>
    {:else}
      <div class="row">
        {#if ws.dumper === 'installed'}
          <button class="primary" onclick={() => report(session.runDump(), summarizeDump)} disabled={session.busy}>Run data dump</button>
          <button onclick={() => session.uninstallDumper()} disabled={session.busy}>Uninstall dumper</button>
        {:else}
          <button class="primary" onclick={() => report(session.runJob('dump.install', undefined, 'Install dumper'), (r) => r.message)} disabled={session.busy}>Install dumper</button>
        {/if}
      </div>
    {/if}
  </section>
{/if}

<section class="card" aria-labelledby="support-heading">
  <h2 id="support-heading">Support</h2>
  <p class="hint">Diagnostics list versions, the game build and recent log lines (never game files), ready to paste into a bug report.</p>
  <button onclick={() => session.copyDiagnostics()}>Copy diagnostics</button>
</section>

<style>
  .recent { list-style: none; padding: 0; margin: 0; display: grid; gap: 4px; }
  .outputs { width: 100%; }
</style>
