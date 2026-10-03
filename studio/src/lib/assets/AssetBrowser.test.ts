import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { AssetListParams, AssetRow } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import AssetBrowser from './AssetBrowser.svelte';

const STEGO = 'animals/stego_assets_assets';
const stegoD: AssetRow = {
  ref: `${STEGO}/textures.bundle#1`, bundle: `${STEGO}/textures.bundle`, pathId: 1, type: 'Texture2D', name: 'T_Stego_D',
  containerPath: 'Assets/Art/Animals/Dinosaurs/Stegosaurus/Textures/T_Stego_D.png', guid: '0123456789abcdef0123456789abcdef', script: null,
};

function setup() {
  const rpc = new FakeRpc();
  rpc.on('assets.summary', () => ({
    assets: 5, bundles: 4, warnings: [], failures: 0, missingBundles: 0, stale: false,
    groups: [{ name: STEGO, count: 3 }, { name: 'ui_assets_all.bundle', count: 1 }],
    types: [{ name: 'GameObject', count: 1 }, { name: 'Texture2D', count: 3 }],
  }));
  rpc.on('assets.list', () => ({ rows: [stegoD], total: 1, page: 0, pageSize: 200 }));
  rpc.on('assets.bundles', () => ({ bundles: [{ name: `${STEGO}/prefabs.bundle`, count: 1 }, { name: `${STEGO}/textures.bundle`, count: 2 }] }));
  rpc.on('assets.get', () => ({ asset: stegoD, byteSize: 4096, references: [], fields: { m_Width: 2048 } }));
  rpc.on('assets.preview', () => ({ kind: 'none', files: [], width: null, height: null, format: null, mipCount: null, vertices: null, triangles: null, skinned: null, message: null }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  session.workspace = workspaceStatus({ hasAssetIndex: true });
  return { rpc, platform, session };
}

const lastList = (rpc: FakeRpc) => rpc.callsTo('assets.list').at(-1)?.params as AssetListParams;

describe('AssetBrowser', () => {
  it('lists assets and the groups from the summary', async () => {
    const { session } = setup();
    renderWith(AssetBrowser, session);

    expect(await screen.findByRole('button', { name: 'T_Stego_D' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^stego_assets_assets/ })).toBeInTheDocument();
  });

  it('searches once typing stops', async () => {
    const { rpc, session } = setup();
    renderWith(AssetBrowser, session);
    await screen.findByRole('button', { name: 'T_Stego_D' });

    await fireEvent.input(screen.getByRole('searchbox', { name: 'Search assets' }), { target: { value: 'carcharo' } });

    await waitFor(() => expect(lastList(rpc)).toMatchObject({ filter: 'carcharo', page: 0 }));
  });

  it('opening a group shows its bundles and picking one filters the list', async () => {
    const { rpc, session } = setup();
    renderWith(AssetBrowser, session);
    await screen.findByRole('button', { name: 'T_Stego_D' });

    await fireEvent.click(screen.getByRole('button', { name: `Show bundles in ${STEGO}` }));
    await fireEvent.click(await screen.findByRole('button', { name: /^textures\.bundle/ }));

    await waitFor(() => expect(lastList(rpc)).toMatchObject({ group: STEGO, bundle: `${STEGO}/textures.bundle` }));
  });

  it('the type filter narrows the list', async () => {
    const { rpc, session } = setup();
    renderWith(AssetBrowser, session);
    await screen.findByRole('button', { name: 'T_Stego_D' });

    await fireEvent.change(screen.getByRole('combobox', { name: 'Type' }), { target: { value: 'Texture2D' } });

    await waitFor(() => expect(lastList(rpc)).toMatchObject({ type: 'Texture2D' }));
  });

  it('exports the selected assets in one job', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.export', () => ({ exported: 1, failed: 0, reportPath: 'D:\\ws\\exports\\asset-export-1.json', failures: [] }));
    rpc.on('workspace.status', () => workspaceStatus({ hasAssetIndex: true }));
    renderWith(AssetBrowser, session);
    await screen.findByRole('button', { name: 'T_Stego_D' });

    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select T_Stego_D' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Export selected (1)' }));

    await waitFor(() => expect(rpc.callsTo('assets.export')[0]?.params).toEqual({ refs: [stegoD.ref] }));
    await waitFor(() => expect(session.notice).toContain('Exported 1 assets'));
  });

  it('clicking a name shows its details', async () => {
    const { session } = setup();
    renderWith(AssetBrowser, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'T_Stego_D' }));

    expect(await screen.findByRole('heading', { name: 'T_Stego_D' })).toBeInTheDocument();
  });

  it('passes export notes on in the notice', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.export', () => ({
      exported: 1, failed: 0, reportPath: 'D:\ws\exports\asset-export-1.json', failures: [],
      notes: ['Textures kept in other bundles need a newer asset index: click Index assets on the Home tab.'],
    }));
    rpc.on('workspace.status', () => workspaceStatus({ hasAssetIndex: true }));
    renderWith(AssetBrowser, session);
    await screen.findByRole('button', { name: 'T_Stego_D' });

    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select T_Stego_D' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Export selected (1)' }));

    await waitFor(() => expect(session.notice).toContain('click Index assets'));
  });

  it('says when bundles were downloaded since the last index', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.summary', () => ({ assets: 5, bundles: 4, groups: [], types: [], warnings: [], failures: 0, missingBundles: 2, newBundles: 2, stale: false }));
    renderWith(AssetBrowser, session);

    expect(await screen.findByText(/2 bundles were downloaded since the last index/)).toBeInTheDocument();
  });
});
