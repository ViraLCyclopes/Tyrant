import { screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import AssetsView from './AssetsView.svelte';

describe('AssetsView', () => {
  it('asks for an asset index first', () => {
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    session.workspace = workspaceStatus({ hasAssetIndex: false });
    renderWith(AssetsView, session);

    expect(screen.getByText(/not indexed yet/)).toBeInTheDocument();
    expect(screen.queryByRole('searchbox')).toBeNull();
  });

  it('shows the browser once assets are indexed', () => {
    const rpc = new FakeRpc();
    rpc.on('assets.summary', () => ({ assets: 0, bundles: 0, groups: [], types: [], warnings: [], failures: 0, missingBundles: 0, stale: false }));
    rpc.on('assets.list', () => ({ rows: [], total: 0, page: 0, pageSize: 200 }));
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    session.workspace = workspaceStatus({ hasAssetIndex: true });
    renderWith(AssetsView, session);

    expect(screen.getByRole('searchbox', { name: 'Search assets' })).toBeInTheDocument();
  });
});
