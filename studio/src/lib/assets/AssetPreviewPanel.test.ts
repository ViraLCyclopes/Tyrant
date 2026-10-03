import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { AssetRow } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import AssetPreviewPanel from './AssetPreviewPanel.svelte';

vi.mock('./viewer', () => ({ showModels: vi.fn(async () => ({ setSkeleton: vi.fn(), dispose: vi.fn() })) }));

const asset = (type: string): AssetRow => ({ ref: `b#${type}`, bundle: 'b', pathId: 1, type, name: `A ${type}`, containerPath: null, guid: null, script: null });

function setup() {
  const rpc = new FakeRpc();
  rpc.on('assets.preview', (p) =>
    p.ref === 'b#Texture2D'
      ? { kind: 'texture', files: ['D:\\t.png'], width: 4, height: 4, format: 'RGBA32', mipCount: 1, vertices: null, triangles: null, skinned: null, message: null }
      : { kind: 'model', files: ['D:\\m.glb'], width: null, height: null, format: null, mipCount: null, vertices: 3, triangles: 1, skinned: false, message: null },
  );
  return { rpc, session: new Session(rpc, new FakePlatform(), memoryStore()) };
}

describe('AssetPreviewPanel', () => {
  it('previews textures right away', async () => {
    const { rpc, session } = setup();
    renderWith(AssetPreviewPanel, session, { asset: asset('Texture2D') });

    expect(await screen.findByRole('img', { name: 'Texture preview' })).toBeInTheDocument();
    expect(rpc.callsTo('assets.preview')).toHaveLength(1);
  });

  it('loads a 3D preview only when asked', async () => {
    const { rpc, session } = setup();
    renderWith(AssetPreviewPanel, session, { asset: asset('GameObject') });
    expect(rpc.callsTo('assets.preview')).toHaveLength(0);

    await fireEvent.click(screen.getByRole('button', { name: 'Load 3D preview' }));

    await waitFor(() => expect(rpc.callsTo('assets.preview')).toHaveLength(1));
    expect(await screen.findByLabelText('3D preview')).toBeInTheDocument();
  });

  it('shows nothing for assets without a visual preview', () => {
    const { rpc, session } = setup();
    renderWith(AssetPreviewPanel, session, { asset: asset('MonoBehaviour') });

    expect(screen.queryByRole('region', { name: 'Preview' })).toBeNull();
    expect(rpc.callsTo('assets.preview')).toHaveLength(0);
  });
});
