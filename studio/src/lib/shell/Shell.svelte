<script lang="ts">
  import { setContext, untrack } from 'svelte';
  import ErrorBanner from '$lib/components/ErrorBanner.svelte';
  import { getSession } from '$lib/stores/session.svelte';
  import AboutDialog from './AboutDialog.svelte';
  import LogPanel from './LogPanel.svelte';
  import MenuBar from './MenuBar.svelte';
  import type { Menu, MenuItem } from './menu';
  import PreferencesDialog from './PreferencesDialog.svelte';
  import { SHELL_KEY, type ShellState } from './shellState.svelte';
  import StatusLine from './StatusLine.svelte';
  import TabScope from './TabScope.svelte';
  import TabStrip from './TabStrip.svelte';

  let { shell }: { shell: ShellState } = $props();
  const session = getSession();
  setContext(SHELL_KEY, untrack(() => shell));
  let showPrefs = $state(false);
  let showAbout = $state(false);

  const activeTab = $derived(shell.activeId ? shell.tab(shell.activeId) : null);
  const logPath = $derived(session.workspace ? `${session.workspace.dir}\\logs\\studio.log` : null);

  async function pickAndOpen() {
    const dir = await session.platform.pickFolder('Open a workspace folder');
    if (dir) await session.openWorkspace(dir);
  }

  async function pickAndCreate() {
    const dir = await session.platform.pickFolder('Choose an empty folder for the new workspace');
    if (dir) await session.createWorkspace(dir);
  }

  async function pickGame() {
    const dir = await session.platform.pickFolder('Select the Prehistoric Kingdom folder');
    if (!dir) return;
    if (session.workspace) await session.openWorkspace(session.workspace.dir, dir);
    else await session.detectGame(dir);
  }

  const separator: MenuItem = { separator: true };

  const menus = $derived<Menu[]>([
    {
      label: 'File',
      items: () => [
        { label: 'New workspace…', run: pickAndCreate, enabled: () => !session.busy && session.install !== null },
        { label: 'Open workspace…', run: pickAndOpen, enabled: () => !session.busy },
        ...session.recent.map((dir): MenuItem => ({ label: `Recent: ${dir}`, run: () => void session.openWorkspace(dir), enabled: () => !session.busy })),
        separator,
        { label: 'Change game folder…', run: pickGame, enabled: () => !session.busy },
        { label: 'New mod…', run: () => void shell.openTool('mods'), enabled: () => session.workspace !== null },
      ],
    },
    {
      label: 'Edit',
      items: () => [
        { label: 'Undo', shortcut: 'Ctrl+Z', run: () => {}, enabled: () => false },
        { label: 'Redo', shortcut: 'Ctrl+Y', run: () => {}, enabled: () => false },
        separator,
        { label: 'Preferences…', shortcut: 'Ctrl+,', run: () => (showPrefs = true) },
      ],
    },
    {
      label: 'View',
      items: () => [
        { label: 'Show log', shortcut: 'Ctrl+L', run: () => shell.toggleLog(), checked: () => (shell.activeId ? shell.panel(shell.activeId).open : false) },
        { label: 'Log at the bottom', run: () => shell.setPrefs({ logPosition: 'bottom' }), checked: () => shell.prefs.logPosition === 'bottom' },
        { label: 'Log at the side', run: () => shell.setPrefs({ logPosition: 'side' }), checked: () => shell.prefs.logPosition === 'side' },
      ],
    },
    ...(activeTab?.menus ?? []),
    {
      label: 'Help',
      items: () => [
        { label: 'Show studio.log', run: () => void (logPath && session.platform.reveal(logPath)), enabled: () => logPath !== null },
        { label: 'Copy diagnostics', run: () => void session.copyDiagnostics(activeTab ?? undefined) },
        { label: 'About Tyrant', run: () => (showAbout = true) },
      ],
    },
  ]);

  function onKey(e: KeyboardEvent) {
    if (!e.ctrlKey || e.altKey) return;
    const key = e.key.toLowerCase();
    if (e.key === 'Tab') shell.cycle(e.shiftKey ? -1 : 1);
    else if (key === 'w' && shell.activeId) shell.close(shell.activeId);
    else if (key === 'l') shell.toggleLog();
    else if (e.key === ',') showPrefs = true;
    else return;
    e.preventDefault();
  }

  // Remember the open tabs whenever they, their titles or the shown one change.
  $effect(() => {
    void shell.tabs.map((t) => t.title);
    void shell.activeId;
    untrack(() => shell.save());
  });
</script>

<svelte:window onkeydown={onKey} />

<div class="shell">
  <TabStrip {shell} />
  <MenuBar {menus} />
  {#if session.error}
    <ErrorBanner error={session.error} onFix={(fix) => session.applyFix(fix)} onDismiss={() => (session.error = null)} />
  {/if}
  <div class="body" class:side={shell.prefs.logPosition === 'side'}>
    <div class="tabs">
      {#each shell.tabs as record (record.id)}
        <TabScope tab={shell.tab(record.id)} def={shell.registry.get(record.toolId)!} />
      {/each}
    </div>
    {#if activeTab && shell.panel(activeTab.id).open}
      {@const id = activeTab.id}
      <LogPanel
        records={session.log.forTab(id)}
        position={shell.prefs.logPosition}
        size={shell.panel(id).size}
        onResize={(size) => shell.setPanel(id, { size })}
        onClear={() => session.log.clear(id)}
        onCopy={() => void session.platform.copy(session.log.forTab(id).map((r) => `${r.level}\t${r.message}${r.detail ? ` (${r.detail})` : ''}`).join('\n'))}
        onReveal={logPath ? () => void session.platform.reveal(logPath!) : null}
      />
    {/if}
  </div>
  <StatusLine onStale={() => shell.openTool('workspace')} />
</div>

{#if showPrefs}
  <PreferencesDialog {shell} introHidden={shell.introHidden} onIntro={(hidden) => shell.setIntroHidden(hidden)} onClose={() => (showPrefs = false)} />
{/if}
{#if showAbout}<AboutDialog onClose={() => (showAbout = false)} />{/if}

<style>
  .shell { display: grid; grid-template-rows: auto auto auto 1fr auto; height: 100vh; }
  .body { display: flex; flex-direction: column; min-height: 0; }
  .body.side { flex-direction: row; }
  .tabs { flex: 1; min-height: 0; min-width: 0; position: relative; }
</style>
