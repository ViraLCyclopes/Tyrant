import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import type { AssetRow } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, messages } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import AssetDetail from './AssetDetail.svelte';

const row: AssetRow = {
  ref: 'stego/textures.bundle#1', bundle: 'stego/textures.bundle', pathId: 1, type: 'MonoBehaviour', name: 'AnimalData_Stego',
  containerPath: 'Assets/Data/AnimalData_Stego.asset', guid: '0123456789abcdef0123456789abcdef', script: 'AnimalData',
};

function setup() {
  const rpc = new FakeRpc();
  rpc.on('assets.get', () => ({
    asset: row,
    byteSize: 4096,
    references: [
      { field: 'diffuse', ref: 'stego/textures.bundle#2', type: 'Texture2D', name: 'T_Stego_D', external: null },
      { field: 'm_Script', ref: null, type: null, name: null, external: 'CAB-abc #77' },
    ],
    fields: { m_Name: 'AnimalData_Stego', cost: 28368 },
  }));
  const platform = new FakePlatform();
  return { rpc, platform, session: new Session(rpc, platform, memoryStore()) };
}

describe('AssetDetail', () => {
  it('shows the keys and copies them', async () => {
    const { platform, session } = setup();
    renderWith(AssetDetail, session, { ref: row.ref, onOpen: vi.fn() });

    expect(await screen.findByRole('heading', { name: 'AnimalData_Stego' })).toBeInTheDocument();
    expect(screen.getByText('4.0 KB')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Copy Addressables path' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Copy GUID' }));

    expect(platform.copied).toEqual([row.containerPath, row.guid]);
    await waitFor(() => expect(messages(session, 'tab-test').join('\n')).toContain('GUID copied.'));
  });

  it('follows a reference to the asset it points at', async () => {
    const { session } = setup();
    const onOpen = vi.fn();
    renderWith(AssetDetail, session, { ref: row.ref, onOpen });

    await fireEvent.click(await screen.findByRole('button', { name: 'T_Stego_D' }));

    expect(onOpen).toHaveBeenCalledWith('stego/textures.bundle#2');
    expect(screen.getByText('CAB-abc #77')).toBeInTheDocument();
  });

  it('says when only the first references are listed', async () => {
    const { rpc, session } = setup();
    const references = Array.from({ length: 500 }, (_, i) => ({ field: `m_${i}`, ref: null, type: null, name: null, external: `CAB-abc #${i}` }));
    rpc.on('assets.get', () => ({ asset: row, byteSize: 1, references, fields: {}, referencesCapped: true }));
    renderWith(AssetDetail, session, { ref: row.ref, onOpen: vi.fn() });

    expect(await screen.findByRole('heading', { name: 'References (first 500)' })).toBeInTheDocument();
  });

  it('counts every reference when the list is whole', async () => {
    const { session } = setup();
    renderWith(AssetDetail, session, { ref: row.ref, onOpen: vi.fn() });

    expect(await screen.findByRole('heading', { name: 'References (2)' })).toBeInTheDocument();
  });

  it('shows the fields', async () => {
    const { session } = setup();
    renderWith(AssetDetail, session, { ref: row.ref, onOpen: vi.fn() });

    expect(await screen.findByText('cost:')).toBeInTheDocument();
    expect(screen.getByText('28368')).toBeInTheDocument();
  });

  it('says when the asset could not be loaded', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.get', () => {
      throw new RpcError("Bundle 'stego/textures.bundle' no longer exists.", 'ASSET_NOT_FOUND', 'REFRESH_WORKSPACE');
    });
    renderWith(AssetDetail, session, { ref: row.ref, onOpen: vi.fn() });

    expect(await screen.findByText(/could not be loaded/)).toBeInTheDocument();
    expect(screen.queryByText('Loading…')).toBeNull();
  });
});
