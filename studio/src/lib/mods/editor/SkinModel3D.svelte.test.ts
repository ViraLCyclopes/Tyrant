import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { flushSync } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type { AssetPreview, ModSkinDto } from '$lib/rpc/types.gen';
import { TAB_KEY } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { SESSION_KEY, Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { testTab } from '$lib/test/fixtures';
import type { Variant } from './colors';
import SkinModel3D from './SkinModel3D.svelte';

const viewer = vi.hoisted(() => ({
  showModels: vi.fn(), setColors: vi.fn(), setAnimalMaps: vi.fn(), setSkeleton: vi.fn(), setTextures: vi.fn(), setSkin: vi.fn(),
  setGround: vi.fn(), setSky: vi.fn(), frame: vi.fn(), dispose: vi.fn(), pause: vi.fn(), resume: vi.fn(), setIridescence: vi.fn(),
}));
vi.mock('$lib/assets/viewer', () => ({ showModels: viewer.showModels }));

const model: AssetPreview = {
  kind: 'model', files: ['D:\\c\\Acro.glb'], width: null, height: null, format: null, mipCount: null, vertices: 1, triangles: 1, skinned: true, message: null,
  materials: [], skins: null,
};

describe('SkinModel3D (props that change)', () => {
  it('an edit to the skin keeps the chosen sex', async () => {
    const rpc = new FakeRpc()
      .on('mods.skinModel', (p) => ({ prefabRef: 'acro#1', maps: { diffuse: `D:\\m\\${p.sex}_D.png` } }))
      .on('mods.sampleColors', () => ({ a: null, b: null, secondary: null, eye: null, strength: 1, softness: 0.25, hue: 0, saturation: 0, value: 0 }))
      .on('assets.preview', () => model);
    const { showModels, ...methods } = viewer;
    showModels.mockResolvedValue({ ...methods, failures: [] });
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const skin = { id: 'blue', male: { diffuse: 'skins/blue/male_D.png' } } as unknown as ModSkinDto;
    const props = $state({ doc: { id: 'red-spot', detail: { revision: 'r1' } } as never, skin, colorsJson: null as string | null, variant: 'normal' as Variant, materialsJson: null as string | null });
    render(SkinModel3D, { props, context: new Map<symbol, unknown>([[SESSION_KEY, session], [TAB_KEY, testTab(session)]]) });
    await waitFor(() => expect(viewer.setAnimalMaps).toHaveBeenCalled());

    await fireEvent.click(screen.getByRole('radio', { name: 'Infant' }));
    props.skin = { ...skin, name: 'Blue 2' } as unknown as ModSkinDto; // every edit hands down a new skin object
    props.colorsJson = '{"pattern":{"a":"#ff0000"}}';
    flushSync();

    expect((screen.getByRole('radio', { name: 'Infant' }) as HTMLInputElement).checked).toBe(true);
  });

  it('a material draft is previewed: its iridescence goes on the model with the ramp as a URL', async () => {
    const preview = { on: true, strength: 0.25, scale: -0.25, power: -0.3, bias: 1.3, ramp: 'D:\\p\\ramp.png', mask: null, maskSt: [1, 1, 0, 0], smoothness: null };
    const rpc = new FakeRpc()
      .on('mods.skinModel', () => ({ prefabRef: 'acro#1', maps: { diffuse: 'D:\\m\\male_D.png' } }))
      .on('mods.sampleColors', () => ({ a: null, b: null, secondary: null, eye: null, strength: 1, softness: 0.25, hue: 0, saturation: 0, value: 0 }))
      .on('mods.materialPreview', () => preview)
      .on('assets.preview', () => model);
    viewer.setIridescence.mockReset();
    const { showModels, ...methods } = viewer;
    showModels.mockResolvedValue({ ...methods, failures: [] });
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const skin = { id: 'blue', male: { diffuse: 'skins/blue/male_D.png' } } as unknown as ModSkinDto;
    const props = $state({ doc: { id: 'red-spot', detail: { revision: 'r1' } } as never, skin, colorsJson: null as string | null, variant: 'normal' as Variant, materialsJson: null as string | null });
    render(SkinModel3D, { props, context: new Map<symbol, unknown>([[SESSION_KEY, session], [TAB_KEY, testTab(session)]]) });
    // Dressed: the saved materials are previewed (no draft yet).
    await waitFor(() => expect(viewer.setIridescence).toHaveBeenCalled());
    expect(rpc.callsTo('mods.materialPreview')[0].params).toEqual({ id: 'red-spot', skin: 'blue', materials: null, sex: 'male' });

    const draft = '{"Acro":{"iridescence":{"strength":0.25}}}';
    props.materialsJson = draft;
    flushSync();
    await waitFor(() => expect(rpc.callsTo('mods.materialPreview').at(-1)!.params).toEqual({ id: 'red-spot', skin: 'blue', materials: draft, sex: 'male' }));
    await waitFor(() => expect(viewer.setIridescence).toHaveBeenLastCalledWith(expect.objectContaining({ ramp: 'asset://D:\\p\\ramp.png', mask: null })));
    expect(viewer.setIridescence.mock.lastCall![0].uniforms).toMatchObject({ on: true, strength: 0.25, shaping: [-0.25, -0.3, 1.3] });
  });

  it('an image re-imported under the same name (files stamp only) previews the material again', async () => {
    const preview = { on: true, strength: 0.25, scale: -0.25, power: -0.3, bias: 1.3, ramp: 'D:\\p\\ramp.png', mask: null, maskSt: [1, 1, 0, 0], smoothness: null };
    const rpc = new FakeRpc()
      .on('mods.skinModel', () => ({ prefabRef: 'acro#1', maps: { diffuse: 'D:\\m\\male_D.png' } }))
      .on('mods.sampleColors', () => ({ a: null, b: null, secondary: null, eye: null, strength: 1, softness: 0.25, hue: 0, saturation: 0, value: 0 }))
      .on('mods.materialPreview', () => preview)
      .on('assets.preview', () => model);
    const { showModels, ...methods } = viewer;
    showModels.mockResolvedValue({ ...methods, failures: [] });
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const skin = { id: 'blue', male: { diffuse: 'skins/blue/male_D.png' } } as unknown as ModSkinDto;
    const props = $state({ doc: { id: 'red-spot', detail: { revision: 'r1', filesStamp: 'a' } } as { id: string; detail: { revision: string; filesStamp: string } },
      skin, colorsJson: null as string | null, variant: 'normal' as Variant, materialsJson: null as string | null });
    render(SkinModel3D, { props: props as never, context: new Map<symbol, unknown>([[SESSION_KEY, session], [TAB_KEY, testTab(session)]]) });
    await waitFor(() => expect(rpc.callsTo('mods.materialPreview').length).toBeGreaterThan(0));
    await new Promise((r) => setTimeout(r, 300)); // the draft effect's first preview settles
    const before = rpc.callsTo('mods.materialPreview').length;

    props.doc.detail.filesStamp = 'b'; // only the stamp: the PNG changed, mod.json did not
    flushSync();

    await waitFor(() => expect(rpc.callsTo('mods.materialPreview').length).toBeGreaterThan(before));
  });
});
