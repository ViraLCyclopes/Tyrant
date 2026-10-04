import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { AssetListParams, AssetRow } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus, messages } from '$lib/test/fixtures';
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
    await waitFor(() => expect(messages(session, 'tab-test').join('\n')).toContain('Exported 1 assets'));
  });

  it('Select all selects every asset on the filter, across pages', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.refs', () => ({ refs: [stegoD.ref, `${STEGO}/textures.bundle#2`, `${STEGO}/prefabs.bundle#3`] }));
    renderWith(AssetBrowser, session);
    await screen.findByRole('button', { name: 'T_Stego_D' });

    await fireEvent.click(screen.getByRole('button', { name: 'Select all' }));

    expect(await screen.findByRole('button', { name: 'Export selected (3)' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Select T_Stego_D' })).toBeChecked();
  });

  it('Clear empties the selection', async () => {
    const { session } = setup();
    renderWith(AssetBrowser, session);
    await screen.findByRole('button', { name: 'T_Stego_D' });
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select T_Stego_D' }));

    await fireEvent.click(screen.getByRole('button', { name: 'Clear' }));

    expect(screen.getByRole('button', { name: 'Export selected (0)' })).toBeDisabled();
  });

  it('counts and exports only the selected assets the filter shows', async () => {
    const { rpc, session } = setup();
    const stegoN: AssetRow = { ...stegoD, ref: `${STEGO}/textures.bundle#2`, pathId: 2, name: 'T_Stego_N' };
    const rex: AssetRow = { ...stegoD, ref: 'rex.bundle#5', bundle: 'rex.bundle', pathId: 5, name: 'T_Rex_D' };
    rpc.on('assets.list', (p) => (p.filter ? { rows: [stegoD, stegoN], total: 2, page: 0, pageSize: 200 } : { rows: [stegoD, stegoN, rex], total: 3, page: 0, pageSize: 200 }));
    rpc.on('assets.refs', () => ({ refs: [stegoD.ref, stegoN.ref] }));
    rpc.on('assets.export', () => ({ exported: 2, failed: 0, reportPath: 'r.json', failures: [] }));
    rpc.on('workspace.status', () => workspaceStatus({ hasAssetIndex: true }));
    renderWith(AssetBrowser, session);
    for (const name of ['T_Stego_D', 'T_Stego_N', 'T_Rex_D']) await fireEvent.click(await screen.findByRole('checkbox', { name: `Select ${name}` }));

    await fireEvent.input(screen.getByRole('searchbox', { name: 'Search assets' }), { target: { value: 'stego' } });
    await fireEvent.click(await screen.findByRole('button', { name: 'Export selected (2)' }));

    await waitFor(() => expect(rpc.callsTo('assets.export')[0]?.params).toEqual({ refs: [stegoD.ref, stegoN.ref] }));
  });

  it('Export group exports every asset of the picked group', async () => {
    const { rpc, session } = setup();
    const refs = [stegoD.ref, `${STEGO}/textures.bundle#2`, `${STEGO}/prefabs.bundle#3`];
    rpc.on('assets.refs', () => ({ refs }));
    rpc.on('assets.export', () => ({ exported: 3, failed: 0, reportPath: 'r.json', failures: [] }));
    rpc.on('workspace.status', () => workspaceStatus({ hasAssetIndex: true }));
    renderWith(AssetBrowser, session);
    await screen.findByRole('button', { name: 'T_Stego_D' });
    expect(screen.getByRole('button', { name: 'Export group' })).toBeDisabled();

    await fireEvent.click(screen.getByRole('button', { name: /^stego_assets_assets/ }));
    await fireEvent.click(screen.getByRole('button', { name: 'Export group' }));

    await waitFor(() => expect(rpc.callsTo('assets.export')[0]?.params).toEqual({ refs }));
    expect(rpc.callsTo('assets.refs').at(-1)?.params).toEqual({ group: STEGO });
  });

  it('an export with failures is a warning, so the tab gets a marker', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.export', () => ({ exported: 0, failed: 1, reportPath: 'D:\\ws\\report.json', failures: [{ ref: stegoD.ref, name: 'T_Stego_D', error: 'unreadable' }] }));
    rpc.on('workspace.status', () => workspaceStatus({ hasAssetIndex: true }));
    renderWith(AssetBrowser, session);
    await screen.findByRole('button', { name: 'T_Stego_D' });

    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select T_Stego_D' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Export selected (1)' }));

    await waitFor(() => expect(session.log.records.some((r) => r.level === 'warn' && r.message.includes('T_Stego_D'))).toBe(true));
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
      notes: ['Textures kept in other bundles need a newer asset index: click Index assets on the Workspace tab.'],
    }));
    rpc.on('workspace.status', () => workspaceStatus({ hasAssetIndex: true }));
    renderWith(AssetBrowser, session);
    await screen.findByRole('button', { name: 'T_Stego_D' });

    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select T_Stego_D' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Export selected (1)' }));

    await waitFor(() => expect(messages(session, 'tab-test').join('\n')).toContain('click Index assets'));
  });

  it('says when bundles were downloaded since the last index', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.summary', () => ({ assets: 5, bundles: 4, groups: [], types: [], warnings: [], failures: 0, missingBundles: 2, newBundles: 2, stale: false }));
    renderWith(AssetBrowser, session);

    expect(await screen.findByText(/2 bundles were downloaded since the last index/)).toBeInTheDocument();
  });

  it('shows a built-in file group by a readable name', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.summary', () => ({
      assets: 6, bundles: 4, warnings: [], failures: 0, missingBundles: 0, stale: false,
      groups: [{ name: STEGO, count: 3 }, { name: '@data/sharedassets0.assets', count: 1 }],
      types: [{ name: 'Texture2D', count: 4 }],
    }));
    renderWith(AssetBrowser, session);

    expect(await screen.findByRole('button', { name: /Built-in · sharedassets0/ })).toBeInTheDocument();
  });
});
