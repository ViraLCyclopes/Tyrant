import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AssetPreview } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import ModelPreview from './ModelPreview.svelte';

const viewer = vi.hoisted(() => ({ setSkeleton: vi.fn(), dispose: vi.fn(), showModels: vi.fn() }));
vi.mock('./viewer', () => ({ showModels: viewer.showModels }));

const preview: AssetPreview = {
  kind: 'model', files: ['D:\\p\\Body.glb', 'D:\\p\\Eyes.glb'], width: null, height: null, format: null, mipCount: null,
  vertices: 12000, triangles: 8000, skinned: true, message: null,
};

describe('ModelPreview', () => {
  beforeEach(() => {
    viewer.showModels.mockReset();
    viewer.setSkeleton.mockReset();
  });

  it('loads every part into the viewer and toggles the skeleton', async () => {
    viewer.showModels.mockResolvedValue({ setSkeleton: viewer.setSkeleton, dispose: viewer.dispose });
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    renderWith(ModelPreview, session, { preview });

    await waitFor(() => expect(viewer.showModels).toHaveBeenCalled());
    expect(viewer.showModels.mock.calls[0][1]).toEqual(['asset://D:\\p\\Body.glb', 'asset://D:\\p\\Eyes.glb']);
    expect(screen.getByText('12,000 vertices · 8,000 triangles · 2 part(s)')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Show skeleton' }));

    expect(viewer.setSkeleton).toHaveBeenCalledWith(true);
  });

  it('explains when the 3D view cannot start', async () => {
    viewer.showModels.mockRejectedValue(new Error('WebGL is not available'));
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    renderWith(ModelPreview, session, { preview });

    expect(await screen.findByText(/WebGL is not available/)).toBeInTheDocument();
  });
});
