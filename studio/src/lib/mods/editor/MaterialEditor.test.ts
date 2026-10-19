import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModMaterialInfo, ModSkinDto } from '$lib/rpc/types.gen';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import MaterialEditor from './MaterialEditor.svelte';
import { ModDoc } from './modDoc.svelte';

const info = (over: Partial<ModMaterialInfo> = {}): ModMaterialInfo => ({
  name: 'Titanoboa',
  animal: 'Titanoboa',
  properties: [
    { key: '_Smoothness', group: 'Surface', label: 'Smoothness', kind: 'number', min: 0, max: 1, inherited: '0.42', source: 'game' },
    { key: '_Specularity', group: 'Surface', label: 'Specularity', kind: 'number', min: 0, max: 1, inherited: null, source: 'default' },
    { key: '_FresnelColor', group: 'Rim / fresnel', label: 'Rim colour', kind: 'color', min: 0, max: 0, inherited: '"#ffffff"', source: 'game' },
  ],
  inheritedIridescenceJson: null,
  gameHasIridescence: false,
  problem: null,
  ...over,
});

const skinsWith = (materialsJson: string | null): ModSkinDto[] =>
  modDetail().skins.map((s) => (s.id === 'blue' ? { ...s, species: 'Titanoboa', materialsJson } : s));

async function setup({ materialsJson = null, onDraft }: { materialsJson?: string | null; onDraft?: (json: string | null) => void } = {}) {
  let saves = 0;
  const rpc = new FakeRpc()
    .on('mods.get', () => modDetail({ skins: skinsWith(materialsJson) }))
    .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
    .on('mods.materialInfo', () => info())
    .on('mods.thinFilm', (p) => ({ stops: Array.from({ length: p.count ?? 32 }, (_, i) => `#${(i * 7).toString(16).padStart(2, '0')}4080`) }))
    .on('mods.setMaterials', (p) => modDetail({ revision: `r-${++saves}`, skins: skinsWith(p.materials ?? null) }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  renderWith(MaterialEditor, session, { doc, skin: doc.detail!.skins[0], onDraft }, tab);
  await screen.findByText('Material: Titanoboa');
  return { rpc, platform, session, doc };
}

const sent = (rpc: FakeRpc, n = 0) => JSON.parse((rpc.callsTo('mods.setMaterials')[n].params as { materials: string }).materials);

describe('MaterialEditor', () => {
  it('turning iridescence on for a species without it starts visible: strength 0.25 and Microraptor-like shaping', async () => {
    const { rpc } = await setup();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Iridescence' }));
    await waitFor(() => expect(rpc.callsTo('mods.setMaterials')).toHaveLength(1));
    expect(rpc.callsTo('mods.setMaterials')[0].params).toMatchObject({ skin: 'blue' });
    expect(sent(rpc)).toEqual({ Titanoboa: { iridescence: { strength: 0.25, scale: -0.25, power: -0.3, bias: 1.3 } } });
  });

  it('Real iridescence saves a thin-film ramp and shows its 32 stops', async () => {
    const { rpc } = await setup({ materialsJson: '{"Titanoboa":{"iridescence":{"strength":0.3}}}' });
    await fireEvent.click(screen.getByRole('button', { name: 'Real iridescence (thin film)' }));
    await waitFor(() => expect(rpc.callsTo('mods.setMaterials')).toHaveLength(1));
    expect(sent(rpc)).toEqual({ Titanoboa: { iridescence: { strength: 0.3, ramp: { thinFilm: { thickness: 420 } } } } });
    const ramp = await screen.findByRole('group', { name: 'Ramp' });
    await waitFor(() => expect(within(ramp).getAllByLabelText(/^Ramp colour \d+$/)).toHaveLength(32));
    expect(rpc.callsTo('mods.thinFilm').at(-1)?.params).toMatchObject({ thickness: 420, count: 32 });
  });

  it('editing a thin-film stop by hand turns the ramp into stops', async () => {
    const { rpc } = await setup({ materialsJson: '{"Titanoboa":{"iridescence":{"strength":0.3,"ramp":{"thinFilm":{"thickness":420}}}}}' });
    const first = await screen.findByLabelText('Ramp colour 1');
    await waitFor(() => expect(screen.getAllByLabelText(/^Ramp colour \d+$/)).toHaveLength(32));
    await fireEvent.change(first, { target: { value: '#ff0000' } });
    await waitFor(() => expect(rpc.callsTo('mods.setMaterials')).toHaveLength(1));
    const stops = sent(rpc).Titanoboa.iridescence.ramp.stops as string[];
    expect(stops).toHaveLength(32);
    expect(stops[0]).toBe('#ff0000');
  });

  it('Import image picks a PNG and copies it in as the ramp', async () => {
    const { rpc, platform } = await setup({ materialsJson: '{"Titanoboa":{"iridescence":{"strength":0.3}}}' });
    rpc.on('mods.setMaterialFile', () => modDetail({ revision: 'r2', skins: skinsWith('{"Titanoboa":{"iridescence":{"strength":0.3,"ramp":"materials/blue/iridescence-ramp.png"}}}') }));
    platform.files = ['C:\paint\ramp.png'];
    await fireEvent.click(within(screen.getByRole('radiogroup', { name: 'Ramp source' })).getByRole('radio', { name: 'Import image' }));
    await waitFor(() => expect(rpc.callsTo('mods.setMaterialFile')[0]?.params).toMatchObject({ skin: 'blue', material: 'Titanoboa', part: 'ramp', png: 'C:\paint\ramp.png' }));
    expect(await screen.findByText('materials/blue/iridescence-ramp.png')).toBeInTheDocument();
  });

  it("a mask from a map the skin takes from its base offers Edit a copy", async () => {
    const { rpc } = await setup({ materialsJson: '{"Titanoboa":{"iridescence":{"strength":0.3,"mask":{"from":"pattern","channel":"g","invert":false}}}}' });
    rpc.on('mods.copyBaseFile', () => modDetail({ revision: 'r2', skins: skinsWith(null) }));
    expect(screen.getByRole('combobox', { name: 'Mask map' })).toHaveValue('pattern');
    await fireEvent.click(screen.getByRole('button', { name: 'Edit a copy' }));
    await waitFor(() => expect(rpc.callsTo('mods.copyBaseFile')).toHaveLength(1));
    expect(rpc.callsTo('mods.copyBaseFile')[0].params).toMatchObject({ skin: 'blue', sex: 'male', slot: 'pattern' });
  });

  it('resetting a setting removes its key', async () => {
    const { rpc } = await setup({ materialsJson: '{"Titanoboa":{"settings":{"_Smoothness":0.55,"_Specularity":0.3}}}' });
    await fireEvent.click(screen.getByRole('button', { name: 'Reset Smoothness' }));
    await waitFor(() => expect(rpc.callsTo('mods.setMaterials')).toHaveLength(1));
    expect(sent(rpc)).toEqual({ Titanoboa: { settings: { _Specularity: 0.3 } } });
  });

  it('a setting row shows the inherited value and where it comes from', async () => {
    await setup();
    const row = screen.getByRole('group', { name: 'Smoothness' });
    expect(row).toHaveTextContent('0.42');
    expect(row).toHaveTextContent('from game');
  });

  it('setting an override saves it', async () => {
    const { rpc } = await setup();
    await fireEvent.change(screen.getByRole('spinbutton', { name: 'Smoothness' }), { target: { value: '0.7' } });
    await waitFor(() => expect(rpc.callsTo('mods.setMaterials')).toHaveLength(1));
    expect(sent(rpc)).toEqual({ Titanoboa: { settings: { _Smoothness: 0.7 } } });
  });

  it('hands the draft to the preview, keyed by material name', async () => {
    const onDraft = vi.fn();
    await setup({ materialsJson: '{"Titanoboa":{"settings":{"_Smoothness":0.55}}}', onDraft });
    await waitFor(() => expect(onDraft).toHaveBeenLastCalledWith('{"Titanoboa":{"settings":{"_Smoothness":0.55}}}'));
  });
});
