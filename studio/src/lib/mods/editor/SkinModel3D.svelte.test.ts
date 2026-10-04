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
  setGround: vi.fn(), setSky: vi.fn(), frame: vi.fn(), dispose: vi.fn(), pause: vi.fn(), resume: vi.fn(),
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
    const props = $state({ doc: { id: 'red-spot', detail: { revision: 'r1' } } as never, skin, colorsJson: null as string | null, variant: 'normal' as Variant });
    render(SkinModel3D, { props, context: new Map<symbol, unknown>([[SESSION_KEY, session], [TAB_KEY, testTab(session)]]) });
    await waitFor(() => expect(viewer.setAnimalMaps).toHaveBeenCalled());

    await fireEvent.click(screen.getByRole('radio', { name: 'Infant' }));
    props.skin = { ...skin, name: 'Blue 2' } as unknown as ModSkinDto; // every edit hands down a new skin object
    props.colorsJson = '{"pattern":{"a":"#ff0000"}}';
    flushSync();

    expect((screen.getByRole('radio', { name: 'Infant' }) as HTMLInputElement).checked).toBe(true);
  });
});
