import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ModDetail, ModModelDto } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import ModelPage from './ModelPage.svelte';

const viewer = vi.hoisted(() => ({
  showModels: vi.fn(), setColors: vi.fn(), setAnimalMaps: vi.fn(), setSkeleton: vi.fn(), setTextures: vi.fn(), setSkin: vi.fn(),
  setGround: vi.fn(), setSky: vi.fn(), frame: vi.fn(), dispose: vi.fn(), pause: vi.fn(), resume: vi.fn(),
}));
vi.mock('$lib/assets/viewer', () => ({ showModels: viewer.showModels }));

const model: ModModelDto = {
  target: 'Carcharodontosaurus', skin: null, file: 'models/carch-1a2b.glb', stale: false, errors: [],
  warnings: ["The rest pose of bone 'Jaw' differs from the game's"],
  lods: [{ file: 'a.lod0.tmesh', vertices: 9000, index32: false, vanilla: 20000 }, { file: 'a.lod1.tmesh', vertices: 3000, index32: false, vanilla: 7000 }],
};

function setup() {
  const rpc = new FakeRpc().on('mods.modelPreview', () => ({
    prefabRef: 'carch#1', materials: [],
    lods: [{ file: 'D:\\p\\lod0.glb', vertices: 9000 }, { file: 'D:\\p\\lod1.glb', vertices: 3000 }],
  }));
  const { showModels, ...methods } = viewer;
  showModels.mockResolvedValue({ ...methods, failures: [] });
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const doc = { id: 'big-carch', detail: { revision: 'r1', models: [model] } as unknown as ModDetail, edit: vi.fn(async () => true) };
  return { rpc, session, doc, platform };
}

beforeEach(() => Object.values(viewer).forEach((fn) => fn.mockReset()));

describe('ModelPage', () => {
  it('previews LOD 0 and switches to another LOD', async () => {
    const { session, doc } = setup();
    renderWith(ModelPage, session, { doc, model });

    await waitFor(() => expect(viewer.showModels.mock.calls[0]?.[1]).toEqual([{ file: 'D:\\p\\lod0.glb', url: 'asset://D:\\p\\lod0.glb' }]));
    await fireEvent.click(screen.getByRole('button', { name: 'LOD 1' }));
    await waitFor(() => expect(viewer.showModels.mock.calls.at(-1)![1]).toEqual([{ file: 'D:\\p\\lod1.glb', url: 'asset://D:\\p\\lod1.glb' }]));
  });

  it('shows each LOD against the game and the warnings', () => {
    const { session, doc } = setup();
    renderWith(ModelPage, session, { doc, model });

    expect(screen.getByText('LOD 0: 9,000 vertices (game: 20,000)')).toBeInTheDocument();
    expect(screen.getByText(/rest pose of bone 'Jaw'/)).toBeInTheDocument();
  });

  it('Rebuild LODs, Replace… and Remove go through the undoable edits', async () => {
    const { session, doc, platform } = setup();
    platform.files.push('D:\\blender\\carch2.glb');
    const onRemoved = vi.fn();
    renderWith(ModelPage, session, { doc, model, onRemoved });

    await fireEvent.click(screen.getByRole('button', { name: 'Rebuild LODs' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Replace…' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(onRemoved).toHaveBeenCalled());
    expect(doc.edit).toHaveBeenCalledWith('mods.rebuildModels', {});
    expect(doc.edit).toHaveBeenCalledWith('mods.replaceModel', { file: 'D:\\blender\\carch2.glb', target: 'Carcharodontosaurus', skin: null });
    expect(doc.edit).toHaveBeenCalledWith('mods.removeModel', { target: 'Carcharodontosaurus', skin: null });
  });

  it('a model that cannot be previewed says why', async () => {
    const { rpc, session, doc } = setup();
    rpc.on('mods.modelPreview', () => {
      throw new Error('The model has no built levels of detail to show');
    });
    renderWith(ModelPage, session, { doc, model });

    expect(await screen.findByText(/no built levels of detail/)).toBeInTheDocument();
    expect(viewer.showModels).not.toHaveBeenCalled();
  });
});
