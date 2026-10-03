import { screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import DataView from './DataView.svelte';

describe('DataView', () => {
  it('asks for a data dump when there is no data yet', () => {
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    session.workspace = workspaceStatus({ hasData: false });
    renderWith(DataView, session);

    expect(screen.getByText(/No game data yet/)).toBeInTheDocument();
    expect(screen.queryByRole('tab')).toBeNull();
  });

  it('shows the tabs once data exists', () => {
    const rpc = new FakeRpc();
    rpc.on('data.types', () => ({ createdUtc: 'x', buildGuid: 'b', errors: [], types: [] }));
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    session.workspace = workspaceStatus({ hasData: true });
    renderWith(DataView, session);

    expect(screen.getByRole('tab', { name: 'Tables', selected: true })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Browse' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Localization' })).toBeInTheDocument();
  });
});
