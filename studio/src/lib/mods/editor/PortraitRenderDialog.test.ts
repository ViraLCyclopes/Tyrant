import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ModPortraitDto } from '$lib/rpc/types.gen';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import PortraitRenderDialog from './PortraitRenderDialog.svelte';
import { PORTRAIT_SLOTS } from './portraits';

const { fakeViewer, showModels } = vi.hoisted(() => {
  const fakeViewer = {
    failures: [] as string[], setAnimalMaps: vi.fn(), setColors: vi.fn(), portraitView: vi.fn(), setInfant: vi.fn(),
    renderPortrait: vi.fn(async (_w: number, _h: number) => 'iVBORw0KGgo='), whenReady: vi.fn(async () => {}), frame: vi.fn(),
    pause: vi.fn(), resume: vi.fn(), dispose: vi.fn(), setSkeleton: vi.fn(), setTextures: vi.fn(), setSkin: vi.fn(),
    setGround: vi.fn(), setSky: vi.fn(),
  };
  return { fakeViewer, showModels: vi.fn(async () => fakeViewer) };
});
vi.mock('$lib/assets/viewer', () => ({ showModels }));

const COLORS = { a: '#ff0000', b: null, secondary: null, eye: null, strength: 1, softness: 0.25, hue: 0, saturation: 0, value: 0 };

const own = (slot: string, rendered: boolean): ModPortraitDto => ({ slot, file: `portraits/${slot}.png`, own: true, rendered, width: 256, height: 128 });
const game = (slot: string): ModPortraitDto => ({ slot, file: `game/${slot}.png`, own: false, rendered: false, width: 128, height: 64 });

/** The given rows, plus the game's for every other slot. */
function full(given: ModPortraitDto[]): ModPortraitDto[] {
  return PORTRAIT_SLOTS.map((slot) => given.find((p) => p.slot === slot) ?? game(slot));
}

const setOf = (rpc: FakeRpc, method: string, i = 0) => (rpc.callsTo(method)[i].params as { set: Record<string, unknown> }).set;

async function setup(shown: ModPortraitDto[], options: { species?: boolean; female?: boolean; modelFails?: string } = {}) {
  const detail = modDetail();
  if (options.female !== false) detail.skins[0] = { ...detail.skins[0], female: { diffuse: 'skins/blue/female_D.png' }, colorsJson: '{"pattern":{"a":"#ff0000"}}' };
  const rpc = new FakeRpc()
    .on('mods.get', () => detail)
    .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
    .on('mods.skinModel', (p) => {
      if (options.modelFails) throw new Error(options.modelFails);
      return { prefabRef: 'p', maps: { diffuse: `D:\\m\\${p.sex}_D.png` }, ownModel: null };
    })
    .on('mods.speciesModel', (p) => ({ prefabRef: 'p', maps: { diffuse: `D:\\s\\${p.sex}_D.png` }, ownModel: null }))
    .on('assets.preview', () => ({ files: ['C:/x.glb'], materials: [] }))
    .on('mods.sampleColors', () => COLORS)
    .on('mods.setPortraits', () => modDetail({ revision: 'r2' }))
    .on('mods.setSpeciesPortraits', () => modDetail({ revision: 'r2' }));
  const session = new Session(rpc, new FakePlatform(), memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  tab.key = 'red-spot';
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  const target = options.species ? { speciesId: 'example-animal', speciesKey: 'tyrant.red-spot.example-animal' } : { skin: doc.detail!.skins[0] };
  const onclose = vi.fn();
  const view = renderWith(PortraitRenderDialog, session, { doc, target, shown: full(shown), onclose }, tab);
  return { rpc, doc, onclose, view };
}

const useView = async () => {
  const button = await screen.findByRole('button', { name: 'Use this view' });
  await waitFor(() => expect(button).toBeEnabled());
  await fireEvent.click(button);
};

describe('PortraitRenderDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fakeViewer.renderPortrait.mockImplementation(async () => 'iVBORw0KGgo=');
    showModels.mockImplementation(async () => fakeViewer);
  });

  it('renders the normal column from one view, skipping hand-picked slots unless Replace is ticked, in one edit', async () => {
    const shown = [own('adultMale', false), own('adultFemale', true), game('infant')];
    const { rpc, onclose } = await setup(shown);

    await useView();

    await waitFor(() => expect(rpc.callsTo('mods.setPortraits')).toHaveLength(1));
    const set = setOf(rpc, 'mods.setPortraits');
    expect(Object.keys(set).sort()).toEqual(['adultFemale', 'infant']);
    expect(set.infant).toEqual({ data: 'iVBORw0KGgo=', rendered: true });
    expect(fakeViewer.setInfant).toHaveBeenCalledWith(true);
    expect(fakeViewer.renderPortrait).toHaveBeenCalledWith(256, 128);
    await waitFor(() => expect(onclose).toHaveBeenCalled());
  });

  it('with Replace my pictures ticked also renders over a hand-picked slot', async () => {
    const { rpc } = await setup([own('adultMale', false), own('adultFemale', false), own('infant', false)]);
    await fireEvent.click(await screen.findByRole('checkbox', { name: 'Replace my pictures' }));
    await useView();
    await waitFor(() => expect(Object.keys(setOf(rpc, 'mods.setPortraits')).sort()).toEqual(['adultFemale', 'adultMale', 'infant']));
  });

  it('opens on the portrait view with the skin\'s colours, and waits for the textures before each render', async () => {
    const { rpc } = await setup([]);
    await waitFor(() => expect(fakeViewer.portraitView).toHaveBeenCalled());
    expect(rpc.callsTo('mods.sampleColors')[0].params).toMatchObject({ skin: 'blue', colors: '{"pattern":{"a":"#ff0000"}}', seed: 0 });
    expect(fakeViewer.setColors).toHaveBeenCalledWith(COLORS);
    expect(screen.getByLabelText('Portrait framing').tagName).toBe('CANVAS');

    await useView();
    await waitFor(() => expect(fakeViewer.renderPortrait).toHaveBeenCalledTimes(3));
    expect(fakeViewer.whenReady).toHaveBeenCalledTimes(3);
    expect(fakeViewer.whenReady.mock.invocationCallOrder[0]).toBeLessThan(fakeViewer.renderPortrait.mock.invocationCallOrder[0]);
    expect(fakeViewer.setInfant).toHaveBeenLastCalledWith(false);
    expect(fakeViewer.portraitView).toHaveBeenCalledTimes(1); // the camera stays where the user put it
  });

  it('renders only the rows the game shows on a male-only skin', async () => {
    const { rpc } = await setup([], { female: false });
    await useView();
    await waitFor(() => expect(Object.keys(setOf(rpc, 'mods.setPortraits')).sort()).toEqual(['adultMale', 'infant']));
    expect(rpc.callsTo('mods.skinModel').map((c) => (c.params as { sex: string }).sex)).not.toContain('female');
  });

  it('on the species page uses the species model and saves without undo, with no colour sampling', async () => {
    const { rpc, doc } = await setup([], { species: true });
    await useView();
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesPortraits')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSpeciesPortraits')[0].params).toMatchObject({ speciesId: 'example-animal' });
    expect(Object.keys(setOf(rpc, 'mods.setSpeciesPortraits')).sort()).toEqual(['adultFemale', 'adultMale', 'infant']);
    expect(rpc.callsTo('mods.speciesModel').length).toBeGreaterThan(0);
    expect(rpc.callsTo('mods.skinModel')).toHaveLength(0);
    expect(rpc.callsTo('mods.sampleColors')).toHaveLength(0);
    expect(doc.canUndo).toBe(false);
  });

  it('says so when every slot is hand-picked and Replace is unticked, and stays open', async () => {
    const { rpc, onclose } = await setup([own('adultMale', false), own('adultFemale', false), own('infant', false)]);
    await useView();
    expect(await screen.findByText('All three are your own pictures; tick Replace my pictures to render over them.')).toBeInTheDocument();
    expect(rpc.callsTo('mods.setPortraits')).toHaveLength(0);
    expect(fakeViewer.renderPortrait).not.toHaveBeenCalled();
    expect(onclose).not.toHaveBeenCalled();
  });

  it('when the model cannot be loaded, shows why and keeps Use this view disabled', async () => {
    await setup([], { modelFails: 'The 3D view needs the asset index' });
    expect(await screen.findByText('The 3D view needs the asset index')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Use this view' })).toBeDisabled();
    expect(showModels).not.toHaveBeenCalled();
  });

  it('when a render fails, shows the error, puts the adult shape back and stays open', async () => {
    fakeViewer.renderPortrait.mockImplementation(async () => { throw new Error('WebGL lost'); });
    const { rpc, onclose } = await setup([]);
    await useView();
    expect(await screen.findByText('WebGL lost')).toBeInTheDocument();
    expect(fakeViewer.setInfant).toHaveBeenLastCalledWith(false);
    expect(rpc.callsTo('mods.setPortraits')).toHaveLength(0);
    expect(onclose).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Use this view' })).toBeEnabled();
  });

  it('when the save fails, shows the error and stays open', async () => {
    const { rpc, onclose } = await setup([]);
    rpc.on('mods.setPortraits', () => { throw new Error('mod.json is read-only'); });
    await useView();
    expect(await screen.findByText('mod.json is read-only')).toBeInTheDocument();
    expect(onclose).not.toHaveBeenCalled();
  });

  it('Cancel closes without saving, and the viewer is disposed when the dialog goes', async () => {
    const { rpc, onclose, view } = await setup([]);
    await waitFor(() => expect(fakeViewer.portraitView).toHaveBeenCalled());
    await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onclose).toHaveBeenCalled();
    expect(rpc.callsTo('mods.setPortraits')).toHaveLength(0);
    view.unmount();
    expect(fakeViewer.dispose).toHaveBeenCalled();
  });
});

