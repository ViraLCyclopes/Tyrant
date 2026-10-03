<script lang="ts">
  import { onMount } from 'svelte';
  import type { ModCheckReport, ModRow, ModsListResult } from '$lib/rpc/types.gen';
  import { getTab } from '$lib/shell/tab.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import CleanSkins from './CleanSkins.svelte';

  const session = getSession();
  const tab = getTab();
  let list = $state<ModsListResult | null>(null);
  let cleaning = $state(false);
  let checks = $state<Record<string, ModCheckReport>>({});
  let newId = $state('');
  let newName = $state('');

  const STATE_LABEL: Record<string, string> = {
    notInstalled: 'Not installed',
    installed: 'Installed',
    changed: 'Changed since install',
    gameOnly: 'In the game only',
  };

  onMount(() => void refresh());

  async function refresh() {
    const r = await tab.quietly(() => session.rpc.call('mods.list'));
    if (r) list = r;
  }

  async function create() {
    const r = await tab.safely(() => session.rpc.call('mods.create', { id: newId.trim(), name: newName.trim() || null, author: null }));
    if (!r) return;
    list = r;
    newId = '';
    newName = '';
  }

  async function check(row: ModRow) {
    const r = await tab.safely(() => session.rpc.call('mods.check', { id: row.id }));
    if (r) checks = { ...checks, [row.id]: r };
  }

  async function restoreCutouts(row: ModRow) {
    const r = await tab.safely(() => session.rpc.call('mods.restoreCutouts', { id: row.id }));
    if (!r) return;
    const done = r.restored.length
      ? `Restored the see-through parts of ${r.restored.length} PNG${r.restored.length === 1 ? '' : 's'} in '${row.id}'. Install it again to update the game.`
      : `Nothing to restore in '${row.id}'.`;
    tab.info(done);
    for (const problem of r.problems) tab.warn(problem);
    await check(row);
  }

  async function install(row: ModRow) {
    const message = list?.frameworkInstalled
      ? `Copy '${row.name}' into the game (UserData\\Tyrant\\Mods\\${row.id})? No game file is replaced; Remove from game undoes it.`
      : `Install '${row.name}' into the game? This also installs MelonLoader (if needed) and Tyrant's framework. No game file is replaced; Uninstall from game on the Workspace tab undoes everything.`;
    if (!(await session.platform.confirm(message, 'Install to game'))) return;
    const r = await session.runJob('mods.install', { id: row.id }, `Install ${row.id}`, tab);
    if (!r) return;
    tab.info(r.message);
    for (const warning of r.warnings) tab.warn(warning);
    await refresh();
  }

  async function remove(row: ModRow) {
    if (!(await session.platform.confirm(`Remove '${row.name}' from the game?`, 'Remove from game'))) return;
    const r = await tab.safely(() => session.rpc.call('mods.remove', { id: row.id }));
    if (r) list = r;
  }

  function summary(row: ModRow): string {
    const parts = [];
    if (row.replacements) parts.push(`Replaces ${row.replacements} texture${row.replacements === 1 ? '' : 's'}`);
    if (row.skins) parts.push(`Adds ${row.skins} skin${row.skins === 1 ? '' : 's'}`);
    return parts.join(' · ') || 'Empty';
  }

  async function setEnabled(row: ModRow, enabled: boolean, box: HTMLInputElement) {
    const r = await tab.safely(() => session.rpc.call('mods.enable', { id: row.id, enabled }));
    if (r) list = r;
    else box.checked = !enabled; // the change did not happen
  }
</script>

<h1>Mods</h1>
<p class="hint">
  A mod is a folder in your workspace (<code>mods\&lt;id&gt;</code>) with a mod.json and its textures. Installing copies it into the game's
  <code>UserData\Tyrant\Mods</code>; Tyrant's framework applies it while the game runs, so no game file is ever replaced. Add textures from the
  Assets tab with <strong>Replace in a mod…</strong>.
</p>

<form class="row new" onsubmit={(e) => { e.preventDefault(); void create(); }}>
  <input aria-label="Mod id" placeholder="mod id, e.g. red-spot-carcharo" bind:value={newId} />
  <input aria-label="Mod name" placeholder="name (optional)" bind:value={newName} />
  <button type="submit" disabled={!newId.trim() || session.busy}>New mod</button>
</form>
<button class="ghost" onclick={() => (cleaning = !cleaning)}>Clean up skin numbers…</button>
{#if cleaning}<CleanSkins />{/if}

{#if list && !list.frameworkInstalled}
  <p class="hint">Tyrant's framework is not in the game yet; installing a mod installs it.</p>
{/if}

{#if list?.mods.length}
  <table class="grid">
    <thead><tr><th>Mod</th><th>Version</th><th>Content</th><th>State</th><th>On</th><th><span class="visually-hidden">Actions</span></th></tr></thead>
    <tbody>
      {#each list.mods as row (row.id)}
        <tr>
          <td><strong>{row.name}</strong>{#if row.name !== row.id} <span class="hint">{row.id}</span>{/if}{#if row.error}<div class="warn">{row.error}</div>{/if}</td>
          <td>{row.version}</td>
          <td>{summary(row)}</td>
          <td>{STATE_LABEL[row.state] ?? row.state}</td>
          <td>
            {#if row.enabled !== null}
              <input type="checkbox" aria-label="{row.name} on" checked={row.enabled} onchange={(e) => setEnabled(row, e.currentTarget.checked, e.currentTarget)} />
            {/if}
          </td>
          <td class="actions">
            {#if row.state !== 'gameOnly'}
              <button aria-label="Check {row.name}" onclick={() => check(row)}>Check</button>
              <button class="primary" aria-label="Install {row.name} to the game" onclick={() => install(row)} disabled={session.busy}>Install to game</button>
            {/if}
            {#if row.state !== 'notInstalled'}
              <button aria-label="Remove {row.name} from the game" onclick={() => remove(row)} disabled={session.busy}>Remove from game</button>
            {/if}
            {#if row.dir}<button class="ghost" aria-label="Open {row.name} folder" onclick={() => session.platform.reveal(row.dir!)}>Open folder</button>{/if}
          </td>
        </tr>
        {#if checks[row.id]}
          <tr class="check">
            <td colspan="6">
              {#each checks[row.id].errors as error (error)}<div class="warn">{error}</div>{/each}
              {#each checks[row.id].warnings as warning (warning)}<div class="hint">{warning}</div>{/each}
              {#if !checks[row.id].errors.length && !checks[row.id].warnings.length}<div class="hint">No problems found.</div>{/if}
              {#if checks[row.id].missingCutouts?.length}
                <button aria-label="Restore cutouts for {row.name}" onclick={() => restoreCutouts(row)} disabled={session.busy}>Restore cutouts</button>
              {/if}
            </td>
          </tr>
        {/if}
      {/each}
    </tbody>
  </table>
{:else if list}
  <p class="hint">No mods yet. Create one above, or open a texture in the Assets tab and click Replace in a mod….</p>
{/if}

<style>
  .new { gap: 8px; margin: 12px 0; }
  .actions { display: flex; gap: 6px; flex-wrap: wrap; }
</style>
