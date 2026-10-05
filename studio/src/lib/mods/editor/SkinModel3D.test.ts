import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { tick } from 'svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import type { AssetPreview, ModSkinDto } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, testTab } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import SkinModel3D from './SkinModel3D.svelte';

const viewer = vi.hoisted(() => ({
  showModels: vi.fn(),
  setColors: vi.fn(),
  setAnimalMaps: vi.fn(),
  setSkeleton: vi.fn(),
  setTextures: vi.fn(),
  setSkin: vi.fn(),
  setGround: vi.fn(),
  setSky: vi.fn(),
  frame: vi.fn(),
  dispose: vi.fn(),
  pause: vi.fn(),
  resume: vi.fn(),
}));
vi.mock('$lib/assets/viewer', () => ({ showModels: viewer.showModels }));

const skin = { id: 'blue', key: 'red-spot/blue', species: 'Acrocanthosaurus', name: 'Blue', base: 'Alt 1', male: { diffuse: 'skins/blue/male_D.png' } } as unknown as ModSkinDto;
const doc = { id: 'red-spot', detail: { revision: 'r1' } } as never;
const model: AssetPreview = {
  kind: 'model', files: ['D:\\c\\Acro.glb'], width: null, height: null, format: null, mipCount: null, vertices: 1, triangles: 1, skinned: true, message: null,
  materials: [{ name: 'Acro', baseColor: 'D', normal: 'N', skinnable: true, animal: true, cutoff: 0.5, shader: 'AnimalShader', slots: [] }], skins: null,
};

function setup() {
  const rpc = new FakeRpc()
    .on('mods.skinModel', (p) => ({ prefabRef: 'acro#1', maps: { diffuse: `D:\\m\\${p.sex}_D.png` } }))
    .on('mods.sampleColors', (p) => ({ a: '#ff0000', b: '#0000ff', secondary: null, eye: null, strength: (p.seed ?? 1) / 10, softness: 0.25, hue: 0, saturation: 0, value: 0 }))
    .on('assets.preview', () => model);
  const { showModels, ...methods } = viewer;
  showModels.mockResolvedValue({ ...methods, failures: [] });
  return { rpc, session: new Session(rpc, new FakePlatform(), memoryStore()) };
}

beforeEach(() => Object.values(viewer).forEach((fn) => fn.mockReset()));

describe('SkinModel3D', () => {
  it('shows the species model wearing the skin with one animal of the colours', async () => {
    const { session } = setup();
    renderWith(SkinModel3D, session, { doc, skin, colorsJson: null, variant: 'normal' });

    await waitFor(() => expect(viewer.setAnimalMaps).toHaveBeenCalledWith({ diffuse: 'asset://D:\\m\\male_D.png' }));
    await waitFor(() => expect(viewer.setColors).toHaveBeenCalledWith(expect.objectContaining({ a: '#ff0000' })));
    expect(viewer.showModels.mock.calls[0][1]).toEqual([{ file: 'D:\\c\\Acro.glb', url: 'asset://D:\\c\\Acro.glb' }]);
    expect(viewer.showModels.mock.calls[0][2].materials).toEqual(model.materials);
  });

  it('a skin with its own model shows that model, not the species one', async () => {
    const { rpc, session } = setup();
    const own = [{ name: 'AcroNew', baseColor: null, normal: null, skinnable: true, animal: true, cutoff: 0.5, shader: 'AnimalShader', slots: [] }];
    rpc.on('mods.skinModel', (p) => ({ prefabRef: 'acro#1', maps: { diffuse: `D:/m/${p.sex}_D.png` }, ownModel: { file: 'D:/cache/own_lod0.glb', materials: own } }));
    renderWith(SkinModel3D, session, { doc, skin, colorsJson: null, variant: 'normal' });

    await waitFor(() => expect(viewer.setAnimalMaps).toHaveBeenCalledWith({ diffuse: 'asset://D:/m/male_D.png' }));
    expect(viewer.showModels.mock.calls[0][1]).toEqual([{ file: 'D:/cache/own_lod0.glb', url: 'asset://D:/cache/own_lod0.glb' }]);
    expect(viewer.showModels.mock.calls[0][2].materials).toEqual(own);
    expect(rpc.callsTo('assets.preview')).toHaveLength(0);
  });

  it('Infant switches the maps; ↻ samples another animal', async () => {
    const { rpc, session } = setup();
    renderWith(SkinModel3D, session, { doc, skin, colorsJson: null, variant: 'normal' });
    await waitFor(() => expect(viewer.setAnimalMaps).toHaveBeenCalled());

    await fireEvent.click(screen.getByRole('radio', { name: 'Infant' }));
    await waitFor(() => expect(viewer.setAnimalMaps).toHaveBeenLastCalledWith({ diffuse: 'asset://D:\\m\\infant_D.png' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Show another animal' }));
    await waitFor(() => expect(rpc.callsTo('mods.sampleColors').at(-1)?.params).toMatchObject({ seed: 2 }));
    expect(viewer.showModels).toHaveBeenCalledTimes(1); // the same model, re-dressed
  });

  it('a late answer never replaces a newer choice', async () => {
    const { rpc, session } = setup();
    let releaseMale!: () => void;
    rpc.on('mods.skinModel', (p) =>
      p.sex === 'male'
        ? new Promise((r) => (releaseMale = () => r({ prefabRef: 'acro#1', maps: { diffuse: 'D:\\m\\male_D.png' } })))
        : { prefabRef: 'acro#1', maps: { diffuse: 'D:\\m\\female_D.png' } },
    );
    renderWith(SkinModel3D, session, { doc, skin, colorsJson: null, variant: 'normal' });
    await waitFor(() => expect(rpc.callsTo('mods.skinModel')).toHaveLength(1));

    await fireEvent.click(screen.getByRole('radio', { name: 'Female' }));
    await waitFor(() => expect(viewer.setAnimalMaps).toHaveBeenLastCalledWith({ diffuse: 'asset://D:\\m\\female_D.png' }));
    releaseMale();
    await new Promise((r) => setTimeout(r, 0));

    expect(viewer.setAnimalMaps).toHaveBeenLastCalledWith({ diffuse: 'asset://D:\\m\\female_D.png' });
  });

  it('a choice made while the model loads does not start a second model', async () => {
    const { session } = setup();
    let finish!: () => void;
    const { showModels, ...methods } = viewer;
    showModels.mockReturnValue(new Promise((r) => (finish = () => r({ ...methods, failures: [] }))));
    renderWith(SkinModel3D, session, { doc, skin, colorsJson: null, variant: 'normal' });
    await waitFor(() => expect(viewer.showModels).toHaveBeenCalledTimes(1));

    await fireEvent.click(screen.getByRole('radio', { name: 'Female' }));
    await new Promise((r) => setTimeout(r, 0));
    finish();

    await waitFor(() => expect(viewer.setAnimalMaps).toHaveBeenLastCalledWith({ diffuse: 'asset://D:\\m\\female_D.png' }));
    expect(viewer.showModels).toHaveBeenCalledTimes(1); // one engine on the canvas
  });

  it('says what is missing instead of a 3D view', async () => {
    const { rpc, session } = setup();
    rpc.on('mods.skinModel', () => {
      throw new RpcError("The 3D view needs the game's data: on the Workspace tab click Run data dump.", 'DATA_MISSING', 'REFRESH_WORKSPACE');
    });
    renderWith(SkinModel3D, session, { doc, skin, colorsJson: null, variant: 'normal' });

    expect(await screen.findByText(/Run data dump/)).toBeInTheDocument();
    expect(viewer.showModels).not.toHaveBeenCalled();
  });

  it('pauses while its tab is hidden', async () => {
    const { session } = setup();
    const tab = testTab(session);
    tab.active = true;
    renderWith(SkinModel3D, session, { doc, skin, colorsJson: null, variant: 'normal' }, tab);
    await waitFor(() => expect(viewer.setAnimalMaps).toHaveBeenCalled());
    await tick();

    tab.active = false;
    await tick();

    expect(viewer.pause).toHaveBeenCalled();
  });
});
