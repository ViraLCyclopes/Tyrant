import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import { memoryStore } from '$lib/storage';
import { SESSION_KEY, Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { workspaceStatus } from '$lib/test/fixtures';
import { Registry } from './registry';
import { ShellState } from './shellState.svelte';
import Shell from './Shell.svelte';

function setup() {
  const rpc = new FakeRpc();
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const registry = new Registry();
  const stub = () => import('$lib/test/stubs/StubTool.svelte');
  registry.register({ id: 'home', name: 'Home', blurb: '', icon: '', status: 'ready', instances: 'single', closable: false, inDock: false, load: stub });
  registry.register({ id: 'workspace', name: 'Workspace', blurb: '', icon: '', status: 'ready', instances: 'single', load: stub });
  registry.register({ id: 'assets', name: 'Assets', blurb: '', icon: '', status: 'ready', instances: 'many', load: () => import('$lib/test/stubs/StubSearch.svelte') });
  const shell = new ShellState(registry, session.log, memoryStore());
  shell.start();
  render(Shell, { props: { shell }, context: new Map([[SESSION_KEY, session]]) });
  return { rpc, platform, session, shell };
}

describe('Shell', () => {
  it('shows Home first, without a close button', async () => {
    setup();
    expect(await screen.findByRole('tab', { name: 'Home' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByRole('button', { name: 'Close Home' })).toBeNull();
  });

  it('the + button opens another Assets tab, and its close button closes it', async () => {
    const { shell } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'New Assets tab' }));
    expect(await screen.findByRole('tab', { name: 'Assets' })).toHaveAttribute('aria-selected', 'true');
    await fireEvent.click(screen.getByRole('button', { name: 'Close Assets' }));
    expect(shell.tabs.map((t) => t.toolId)).toEqual(['home']);
  });

  it('keeps a hidden tab mounted', async () => {
    const { shell } = setup();
    const id = shell.openTool('assets')!;
    const field = (await screen.findByRole('searchbox', { name: 'Search' })) as HTMLInputElement;
    await fireEvent.input(field, { target: { value: 'carch' } });
    shell.activate(shell.tabs[0].id);
    shell.activate(id);
    await waitFor(() => expect((screen.getByRole('searchbox', { name: 'Search' }) as HTMLInputElement).value).toBe('carch'));
  });

  it('shortcuts work while a search field has focus', async () => {
    const { shell } = setup();
    shell.openTool('assets');
    const field = await screen.findByRole('searchbox', { name: 'Search' });
    field.focus();
    await fireEvent.keyDown(field, { key: 'l', ctrlKey: true });
    expect(shell.panel(shell.activeId!).open).toBe(true);
    await fireEvent.keyDown(field, { key: 'w', ctrlKey: true });
    expect(shell.tabs.map((t) => t.toolId)).toEqual(['home']);
  });

  it('File ▸ Open workspace… opens the picked folder', async () => {
    const { rpc, platform } = setup();
    platform.folders.push('D:\\ws');
    rpc.on('workspace.open', (p) => workspaceStatus({ dir: p.dir }));
    await fireEvent.click(screen.getByRole('button', { name: 'File' }));
    await fireEvent.click(screen.getByRole('menuitem', { name: 'Open workspace…' }));
    await waitFor(() => expect(rpc.callsTo('workspace.open')).toHaveLength(1));
  });

  it("the log panel shows the shown tab's records and core records", async () => {
    const { shell, session } = setup();
    const id = shell.openTool('assets')!;
    shell.tab(id).info('Exported 3 textures.');
    session.log.add({ level: 'info', message: 'Created mod.', tab: null });
    shell.setPanel(id, { open: true });
    const log = within(await screen.findByRole('region', { name: 'Log' }));
    expect(log.getByText('Exported 3 textures.')).toBeInTheDocument();
    expect(log.getByText('Created mod.')).toBeInTheDocument();
  });

  it('marks a background tab that logged a warning', async () => {
    const { shell } = setup();
    const id = shell.openTool('assets')!;
    shell.activate(shell.tabs[0].id);
    shell.tab(id).warn('Careful.');
    expect(await screen.findByLabelText('Assets has warnings')).toBeInTheDocument();
  });

  it('shows a session error above every tab', async () => {
    const { session } = setup();
    session.error = new RpcError('The Tyrant core stopped.', 'SIDECAR_EXITED');
    expect(await screen.findByRole('alert')).toHaveTextContent('The Tyrant core stopped.');
  });

  it("shows an actionable error as a banner in its own tab, with its fix button", async () => {
    const { shell } = setup();
    const id = shell.openTool('assets')!;
    shell.tab(id).fail(new RpcError('No asset index.', 'ASSET_INDEX_MISSING', 'REFRESH_WORKSPACE'));
    expect(await screen.findByRole('alert')).toHaveTextContent('No asset index.');
  });

  it("the status line shows the shown tab's latest message; clicking it opens the log", async () => {
    const { shell } = setup();
    const id = shell.openTool('assets')!;
    shell.tab(id).info("Installed 'red-spot'.");
    const status = await screen.findByRole('button', { name: "Installed 'red-spot'." });
    expect(shell.panel(id).open).toBe(false);
    await fireEvent.click(status);
    expect(shell.panel(id).open).toBe(true);
  });
});
