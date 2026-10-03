import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { installInfo, renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import HomeView from './HomeView.svelte';

function setup() {
  const rpc = new FakeRpc();
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  return { rpc, platform, session };
}

describe('HomeView', () => {
  it('without a workspace offers to create one in a picked folder', async () => {
    const { rpc, platform, session } = setup();
    session.install = installInfo();
    platform.folders.push('D:\\new-ws');
    rpc.on('workspace.create', (p) => workspaceStatus({ dir: p.dir }));
    renderWith(HomeView, session);

    await fireEvent.click(screen.getByRole('button', { name: 'New workspace…' }));

    await waitFor(() => expect(rpc.callsTo('workspace.create')[0]?.params).toEqual({ dir: 'D:\\new-ws', gamePath: 'G:\\PK' }));
    expect(session.workspace?.dir).toBe('D:\\new-ws');
  });

  it('shows the outputs and marks stale ones', () => {
    const { session } = setup();
    session.workspace = workspaceStatus({
      stale: true,
      outputs: [
        { name: 'data', createdUtc: '2026-10-01T10:00:00Z', stale: true },
        { name: 'source/Assembly-CSharp', createdUtc: '2026-10-02T10:00:00Z', stale: false },
      ],
    });
    renderWith(HomeView, session);

    expect(screen.getByText('Game data')).toBeInTheDocument();
    expect(screen.getByText('Code: Assembly-CSharp')).toBeInTheDocument();
    expect(screen.getByText('stale')).toBeInTheDocument();
    expect(screen.getByText(/game was updated/i)).toBeInTheDocument();
  });

  it('installs the dumper when it is missing', async () => {
    const { rpc, session } = setup();
    session.workspace = workspaceStatus();
    rpc.on('dump.install', () => ({ installedLoader: true, message: 'Installed MelonLoader 0.7.3 and the dumper mod (5 files added to the game folder).' }));
    rpc.on('workspace.status', () => workspaceStatus({ dumper: 'installed' }));
    renderWith(HomeView, session);

    await fireEvent.click(screen.getByRole('button', { name: 'Install Tyrant in game' }));

    await waitFor(() => expect(session.notice).toMatch(/Installed MelonLoader/));
    expect(await screen.findByRole('button', { name: 'Run data dump' })).toBeInTheDocument();
  });

  it('run data dump asks first and does nothing when declined', async () => {
    const { rpc, platform, session } = setup();
    session.workspace = workspaceStatus({ dumper: 'installed' });
    platform.confirmAnswer = false;
    renderWith(HomeView, session);

    await fireEvent.click(screen.getByRole('button', { name: 'Run data dump' }));

    await waitFor(() => expect(platform.confirms).toHaveLength(1));
    expect(rpc.callsTo('dump.run')).toHaveLength(0);
  });

  it('disables actions while a job runs', () => {
    const { session } = setup();
    session.workspace = workspaceStatus();
    session.job = { id: 'j1', title: 'Decompile', fraction: 0.2, message: 'Decompiling', cancel: null };
    renderWith(HomeView, session);

    expect(screen.getByRole('button', { name: 'Refresh all' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Install Tyrant in game' })).toBeDisabled();
  });

  it('shows the workspace folder in Explorer', async () => {
    const { platform, session } = setup();
    session.workspace = workspaceStatus();
    renderWith(HomeView, session);

    await fireEvent.click(screen.getByRole('button', { name: 'Show in Explorer' }));

    expect(platform.revealed).toEqual(['D:\\ws']);
  });
});
