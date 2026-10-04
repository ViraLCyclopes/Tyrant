import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import ColourEditor from './ColourEditor.svelte';
import { ModDoc } from './modDoc.svelte';

async function setup({ colorsJson }: { colorsJson: string | null }) {
  const detail = modDetail({ skins: modDetail().skins.map((s) => (s.id === 'blue' ? { ...s, colorsJson } : s)) });
  const rpc = new FakeRpc()
    .on('mods.get', () => detail)
    .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
    .on('mods.colorPreview', () => ({ files: [] }));
  const session = new Session(rpc, new FakePlatform(), memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  renderWith(ColourEditor, session, { doc, skin: doc.detail!.skins[0] }, tab);
  return { rpc, session, doc };
}

describe('ColourEditor', () => {
  it('Normal: setting colour A saves the pattern colour', async () => {
    const { rpc } = await setup({ colorsJson: null });
    rpc.on('mods.setColors', () => modDetail({ revision: 'r2' }));
    const a = screen.getByRole('group', { name: 'Colour A' });
    await fireEvent.click(within(a).getByRole('checkbox', { name: 'From base skin' }));
    await waitFor(() => expect(rpc.callsTo('mods.setColors')[0]?.params).toMatchObject({ skin: 'blue', colors: '{"pattern":{"a":"#808080"}}' }));
  });

  it('Normal: strength and the other pattern fields wait for colour A or B', async () => {
    await setup({ colorsJson: null });
    expect(within(screen.getByRole('group', { name: 'Strength' })).getByRole('checkbox', { name: 'From base skin' })).toBeDisabled();
    expect(screen.getByText(/Set colour A or B first/)).toBeInTheDocument();
  });

  it('Fixed turns a range into one number', async () => {
    const { rpc } = await setup({ colorsJson: '{"pattern":{"a":"#3060ff","strength":[0.6,0.8]}}' });
    rpc.on('mods.setColors', () => modDetail({ revision: 'r2' }));
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Strength fixed' }));
    await waitFor(() => expect(rpc.callsTo('mods.setColors')[0]?.params).toMatchObject({ colors: '{"pattern":{"a":"#3060ff","strength":0.6}}' }));
  });

  it('Exact colours writes the tint as zero', async () => {
    const { rpc } = await setup({ colorsJson: null });
    rpc.on('mods.setColors', () => modDetail({ revision: 'r2' }));
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Exact colours' }));
    await waitFor(() => expect(rpc.callsTo('mods.setColors')).toHaveLength(1));
    const sent = rpc.callsTo('mods.setColors')[0].params as { colors: string };
    expect(JSON.parse(sent.colors)).toEqual({ tint: { hue: 0, saturation: 0, value: 0 } });
  });

  it('a mutation edits its own set, including its own tint', async () => {
    const { rpc } = await setup({ colorsJson: null });
    rpc.on('mods.setColors', () => modDetail({ revision: 'r2' }));
    await fireEvent.click(screen.getByRole('radio', { name: 'Albino' }));
    const eyes = screen.getByRole('group', { name: 'Eyes' });
    await fireEvent.click(within(eyes).getByRole('checkbox', { name: 'From base skin' }));
    await waitFor(() => expect(rpc.callsTo('mods.setColors')[0]?.params).toMatchObject({ colors: '{"albino":{"eye":"#808080"}}' }));
  });

  it('a gradient takes up to eight colours', async () => {
    await setup({ colorsJson: '{"pattern":{"a":["#000000","#111111","#222222","#333333","#444444","#555555","#666666","#777777"]}}' });
    expect(screen.getByRole('button', { name: 'Add a Colour A colour' })).toBeDisabled();
  });

  it('the preview asks for 6 animals with the current colours and shows them', async () => {
    const { rpc } = await setup({ colorsJson: '{"pattern":{"a":"#3060ff"}}' });
    rpc.on('mods.colorPreview', () => ({ files: ['D:\\ws\\cache\\previews\\colors\\a-0.png', 'D:\\ws\\cache\\previews\\colors\\a-1.png'] }));
    await fireEvent.click(screen.getByRole('button', { name: 'Show 6 other animals' }));
    await waitFor(() => expect(screen.getAllByRole('img', { name: /Preview animal/ })).toHaveLength(2));
    expect(rpc.callsTo('mods.colorPreview').at(-1)?.params).toMatchObject({ id: 'red-spot', skin: 'blue', variant: 'normal', count: 6, colors: '{"pattern":{"a":"#3060ff"}}' });
  });

  it('the preview shows the reason when it cannot draw', async () => {
    const { rpc } = await setup({ colorsJson: null });
    rpc.on('mods.colorPreview', () => {
      throw new RpcError('There is no male diffuse to preview it with.', 'TARGET_NOT_FOUND');
    });
    await fireEvent.click(screen.getByRole('button', { name: 'Show 6 other animals' }));
    expect(await screen.findByText(/no male diffuse/)).toBeInTheDocument();
  });
});
