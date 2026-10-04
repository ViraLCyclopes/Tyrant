import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { Registry } from '$lib/shell/registry';
import { SHELL_KEY, ShellState } from '$lib/shell/shellState.svelte';
import { TAB_KEY, Tab } from '$lib/shell/tab.svelte';
import { registerTools } from '$lib/shell/tools';
import { memoryStore, type KeyValueStore } from '$lib/storage';
import { SESSION_KEY, Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { installInfo, workspaceStatus } from '$lib/test/fixtures';
import HomeTool from './HomeTool.svelte';

function setup(store: KeyValueStore = memoryStore()) {
  const rpc = new FakeRpc();
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, store);
  session.install = installInfo();
  return { rpc, platform, session, store };
}

function renderHome(session: Session, store: KeyValueStore, openTool: (id: string) => string | null = () => null) {
  const registry = new Registry();
  registerTools(registry);
  const shell = new ShellState(registry, session.log, store);
  shell.start();
  const tab = new Tab('tab-home', session.log, { retitle: () => {}, openTool });
  return render(HomeTool, { context: new Map<symbol, unknown>([[SESSION_KEY, session], [TAB_KEY, tab], [SHELL_KEY, shell]]) });
}

describe('HomeTool', () => {
  it('dots above the toolbar pick a background picture', async () => {
    const { session, store } = setup();
    renderHome(session, store);

    const dots = screen.getAllByRole('button', { name: /^Picture \d+ of 10$/ });
    expect(dots).toHaveLength(10);
    expect(dots[0]).toHaveAttribute('aria-current', 'true');
    await fireEvent.click(screen.getByRole('button', { name: 'Picture 4 of 10' }));

    expect(screen.getByRole('button', { name: 'Picture 4 of 10' })).toHaveAttribute('aria-current', 'true');
    expect(dots[0]).not.toHaveAttribute('aria-current');
  });

  it('first run: the intro offers to create or open a workspace and cannot be hidden', async () => {
    const { session, platform, rpc, store } = setup();
    platform.folders.push('D:\\new');
    rpc.on('workspace.create', (p) => workspaceStatus({ dir: p.dir }));
    renderHome(session, store);

    expect(screen.queryByRole('button', { name: /Hide this/ })).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'New workspace…' }));
    await waitFor(() => expect(rpc.callsTo('workspace.create')).toHaveLength(1));
  });

  it('hiding the intro is remembered', async () => {
    const { session, store } = setup();
    session.workspace = workspaceStatus();
    const first = renderHome(session, store);
    await fireEvent.click(screen.getByRole('button', { name: /Hide this/ }));
    expect(screen.queryByText(/never replaces game files/)).toBeNull();
    first.unmount();

    renderHome(session, store);
    expect(screen.queryByText(/never replaces game files/)).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'About Tyrant' }));
    expect(screen.getByText(/never replaces game files/)).toBeInTheDocument();
  });

  it('the dock opens ready tools; planned ones are disabled', async () => {
    const { session, store } = setup();
    session.workspace = workspaceStatus();
    const opened: string[] = [];
    renderHome(session, store, (id) => (opened.push(id), 'tab-2'));

    await fireEvent.click(screen.getByRole('button', { name: /^Assets/ }));
    expect(opened).toEqual(['assets']);
    const planned = screen.getByRole('button', { name: /Script mods/ });
    expect(planned).toBeDisabled();
    expect(planned).toHaveTextContent('Not built yet');
  });

  it('model replacements are not offered as a tool to come (they live in Assets and the mod editor)', () => {
    const { session, store } = setup();
    session.workspace = workspaceStatus();
    renderHome(session, store);

    expect(screen.queryByRole('button', { name: /Model replacements/ })).toBeNull();
  });

  it('the status chip says how the workspace is and opens the Workspace tab', async () => {
    const { session, store } = setup();
    session.workspace = workspaceStatus({ dir: 'D:\\TestWork', stale: true });
    const opened: string[] = [];
    renderHome(session, store, (id) => (opened.push(id), 'tab-2'));

    await fireEvent.click(screen.getByRole('button', { name: 'Workspace: TestWork · outputs stale' }));
    expect(opened).toEqual(['workspace']);
  });

  it('without a game, the intro asks for its folder', async () => {
    const { session, platform, rpc, store } = setup();
    session.install = null;
    platform.folders.push('G:\\PK');
    rpc.on('install.detect', () => installInfo());
    renderHome(session, store);

    expect(screen.getByRole('button', { name: 'New workspace…' })).toBeDisabled();
    await fireEvent.click(screen.getByRole('button', { name: 'Find the game folder…' }));
    await waitFor(() => expect(session.install?.rootDir).toBe('G:\\PK'));
  });
});
