import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { tick } from 'svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AssetPreview } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, testTab } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import ModelPreview from './ModelPreview.svelte';

const viewer = vi.hoisted(() => ({
  showModels: vi.fn(),
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
vi.mock('./viewer', () => ({ showModels: viewer.showModels }));

const SKY = [0, 1, 2, 3, 4, 5].map((f) => `D:\\env\\sky_${f}.png`);

const preview: AssetPreview = {
  kind: 'model', files: ['D:\\p\\Body.glb', 'D:\\p\\Eyes.glb'], width: null, height: null, format: null, mipCount: null,
  vertices: 12000, triangles: 8000, skinned: true, message: null,
  materials: [
    { name: 'Acro', baseColor: 'T_Acro_D', normal: 'T_Acro_N', skinnable: true },
    { name: 'Eyes', baseColor: null, normal: null, skinnable: false },
  ],
  skins: [
    { ref: 'b#1', name: 'T_Acro_D', current: true },
    { ref: 'b#2', name: 'T_Acro_alt1_D', current: false },
  ],
};

const texture = (ref: string): AssetPreview => ({
  kind: 'texture', files: [`D:\\skins\\${ref.replace('#', '_')}.png`], width: 4, height: 4, format: 'DXT1', mipCount: 1,
  vertices: null, triangles: null, skinned: null, message: null, materials: null, skins: null,
});

function setup(store = memoryStore()) {
  const rpc = new FakeRpc()
    .on('assets.environments', () => ({
      grounds: [{ id: 'lush-grass', label: 'Lush grass' }, { id: 'sand', label: 'Sand' }],
      skies: [{ id: 'noon', label: 'Noon' }],
    }))
    .on('assets.environment', (p) =>
      p.id === 'noon' ? { id: 'noon', kind: 'sky', files: SKY } : { id: p.id, kind: 'ground', files: [`D:\\env\\${p.id}.png`] },
    )
    .on('assets.preview', (p) => texture(p.ref));
  const { showModels, ...methods } = viewer;
  showModels.mockResolvedValue(methods);
  return { rpc, session: new Session(rpc, new FakePlatform(), store) };
}

describe('ModelPreview', () => {
  beforeEach(() => Object.values(viewer).forEach((fn) => fn.mockReset()));

  it('loads every part with the skinnable materials and toggles the skeleton', async () => {
    const { session } = setup();
    renderWith(ModelPreview, session, { preview });

    await waitFor(() => expect(viewer.showModels).toHaveBeenCalled());
    const [, models, options] = viewer.showModels.mock.calls[0];
    expect(models).toEqual([
      { file: 'D:\\p\\Body.glb', url: 'asset://D:\\p\\Body.glb' },
      { file: 'D:\\p\\Eyes.glb', url: 'asset://D:\\p\\Eyes.glb' },
    ]);
    expect(options.skinnable).toEqual(['Acro']);
    expect(options.fileUrl('D:\\x.png')).toBe('asset://D:\\x.png');
    expect(screen.getByText('12,000 vertices · 8,000 triangles · 2 part(s)')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Show skeleton' }));

    expect(viewer.setSkeleton).toHaveBeenCalledWith(true);
  });

  it('stops drawing while its tab is hidden and resumes when shown', async () => {
    const { session } = setup();
    const tab = testTab(session);
    tab.active = true;
    renderWith(ModelPreview, session, { preview }, tab);
    await waitFor(() => expect(viewer.showModels).toHaveBeenCalled());
    await tick();

    tab.active = false;
    await tick();
    expect(viewer.pause).toHaveBeenCalled();
    tab.active = true;
    await tick();
    expect(viewer.resume).toHaveBeenCalled();
  });

  it('starts with the remembered ground and the default noon sky', async () => {
    const store = memoryStore();
    store.set('tyrant.viewer.ground', 'sand');
    const { session } = setup(store);
    renderWith(ModelPreview, session, { preview });

    await waitFor(() => expect(viewer.setGround).toHaveBeenCalledWith('asset://D:\\env\\sand.png'));
    await waitFor(() => expect(viewer.setSky).toHaveBeenCalledWith(SKY.map((f) => `asset://${f}`)));
  });

  it('removes the ground when None is chosen and remembers that', async () => {
    const store = memoryStore();
    const { session } = setup(store);
    renderWith(ModelPreview, session, { preview });
    const ground = await screen.findByRole('combobox', { name: 'Ground' });
    await waitFor(() => expect(viewer.showModels).toHaveBeenCalled());

    await fireEvent.change(ground, { target: { value: '' } });

    await waitFor(() => expect(viewer.setGround).toHaveBeenLastCalledWith(null));
    expect(store.get('tyrant.viewer.ground')).toBe('');
  });

  it('shows a chosen skin and puts the own one back', async () => {
    const { session } = setup();
    renderWith(ModelPreview, session, { preview });
    const skin = await screen.findByRole('combobox', { name: 'Skin' });
    await waitFor(() => expect(viewer.showModels).toHaveBeenCalled());

    await fireEvent.change(skin, { target: { value: 'b#2' } });
    await waitFor(() => expect(viewer.setSkin).toHaveBeenCalledWith('asset://D:\\skins\\b_2.png'));
    await fireEvent.change(skin, { target: { value: 'b#1' } });

    await waitFor(() => expect(viewer.setSkin).toHaveBeenLastCalledWith(null));
  });

  it('keeps the last skin chosen when an earlier one loads late', async () => {
    const { rpc, session } = setup();
    let release = () => {};
    rpc.on('assets.preview', (p) =>
      p.ref === 'b#2' ? new Promise((resolve) => (release = () => resolve(texture('b#2')))) : texture(p.ref),
    );
    renderWith(ModelPreview, session, { preview });
    const skin = await screen.findByRole('combobox', { name: 'Skin' });
    await waitFor(() => expect(viewer.showModels).toHaveBeenCalled());

    await fireEvent.change(skin, { target: { value: 'b#2' } });
    await fireEvent.change(skin, { target: { value: 'b#1' } });
    release();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(viewer.setSkin).toHaveBeenLastCalledWith(null);
    expect(viewer.setSkin).not.toHaveBeenCalledWith('asset://D:\\skins\\b_2.png');
  });

  it('turns textures off', async () => {
    const { session } = setup();
    renderWith(ModelPreview, session, { preview });
    await waitFor(() => expect(viewer.showModels).toHaveBeenCalled());

    await fireEvent.click(screen.getByRole('checkbox', { name: 'Textures' }));

    expect(viewer.setTextures).toHaveBeenCalledWith(false);
  });

  it('says so when a ground cannot be loaded and keeps the viewer', async () => {
    const { rpc, session } = setup();
    rpc.on('assets.environment', () => {
      throw new Error("'LushGrass_Diffuse' is not in 'sharedassets3.assets' (game updated?)");
    });
    renderWith(ModelPreview, session, { preview });

    expect(await screen.findByText(/The ground could not be loaded: 'LushGrass_Diffuse' is not in/)).toBeInTheDocument();
    expect(viewer.dispose).not.toHaveBeenCalled();
  });

  it('explains when the 3D view cannot start', async () => {
    const { session } = setup();
    viewer.showModels.mockRejectedValue(new Error('WebGL is not available'));
    renderWith(ModelPreview, session, { preview });

    expect(await screen.findByText(/WebGL is not available/)).toBeInTheDocument();
  });
});
