import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { ModSizesDto } from '$lib/rpc/types.gen';
import { RpcError } from '$lib/rpc/client';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import SizeCard from './SizeCard.svelte';

const sizes = (over: Partial<ModSizesDto> = {}): ModSizesDto => ({
  target: 'Spinosaurus', file: 'spinosaurus.json', adult: null, baby: null, male: null, female: null, recordEdits: [],
  nursery: null, breeding: null, gameNursery: [0.925, 1.075], gameBreeding: [0.85, 1.15], name: 'Spinosaurus', baseName: 'Spinosaurus',
  original: { adultLength: 12.1, babyShare: 0.25, skins: [{ male: 1, female: 1.1 }], curve: [[0, 0.25], [1, 1]] }, finalAdultLength: 12.1, ...over,
});

async function setup(dto: ModSizesDto = sizes(), errors: string[] = [], refuse = false) {
  const rpc = new FakeRpc()
    .on('mods.get', () => modDetail())
    .on('mods.check', () => ({ errors, warnings: [], missingCutouts: [] }))
    .on('mods.sizes', () => dto)
    .on('mods.removeSizeEdits', () => modDetail({ revision: 'r3' }))
    .on('mods.setSizes', () => {
      if (refuse) throw new RpcError('The mod folder is read-only.', 'IO_ERROR');
      return modDetail({ revision: 'r2' });
    });
  const session = new Session(rpc, new FakePlatform(), memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  tab.key = 'red-spot';
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  await doc.runCheck();
  const { container } = renderWith(SizeCard, session, { doc, target: 'Spinosaurus' }, tab);
  await screen.findByRole('spinbutton', { name: 'Adult size ×' });
  return { rpc, doc, container };
}

describe('SizeCard', () => {
  it('shows the game originals where the mod sets nothing', async () => {
    await setup();
    expect(screen.getByRole('spinbutton', { name: 'Adult size ×' })).toHaveValue(1);
    expect(screen.getByRole('spinbutton', { name: 'Baby size (share of adult)' })).toHaveValue(0.25);
    // The sex sizes multiply each skin's own: unset is 1×, and the skin's own value is only information.
    expect(screen.getByRole('spinbutton', { name: 'Male size ×' })).toHaveValue(1);
    expect(screen.getByRole('spinbutton', { name: 'Female size ×' })).toHaveValue(1);
    expect(screen.getByText('game: 1 male / 1.1 female')).toBeInTheDocument();
    expect(screen.getByRole('slider', { name: 'Adult size × slider' })).toHaveAttribute('max', '5');
    expect(screen.getByRole('slider', { name: 'Male size × slider' })).toHaveAttribute('min', '0.1');
    expect(screen.getByRole('slider', { name: 'Baby size (share of adult) slider' })).toHaveAttribute('max', '1');
    expect(screen.queryByRole('button', { name: /^Reset/ })).toBeNull();
  });

  it('shows the adult length before and after as a size bar', async () => {
    await setup(sizes({ adult: 0.85, finalAdultLength: 10.3 }));
    expect(screen.getByRole('spinbutton', { name: 'Adult size ×' })).toHaveValue(0.85);
    expect(screen.getByText('12.1 m → 10.3 m')).toBeInTheDocument();
  });

  it('a change sends the set with the changed key; the other keys keep what the mod already has', async () => {
    const { rpc } = await setup(sizes({ baby: 0.4 }));
    const adult = screen.getByRole('spinbutton', { name: 'Adult size ×' });
    await fireEvent.input(adult, { target: { value: '1.2' } });
    await fireEvent.change(adult);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ target: 'Spinosaurus', adult: 1.2, baby: 0.4, male: null, female: null });
  });

  it('nudging Female sends a multiplier of the skin size, not the skin size', async () => {
    const { rpc } = await setup(); // the skin's own female size is 1.1
    const female = screen.getByRole('spinbutton', { name: 'Female size ×' }) as HTMLInputElement;
    female.stepUp();
    await fireEvent.change(female);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ female: 1.01, male: null, adult: null, baby: null });
  });

  it('an explicit 1× and an unset sex size show the same', async () => {
    await setup(sizes({ male: 1 }));
    expect(screen.getByRole('spinbutton', { name: 'Male size ×' })).toHaveValue(1);
    expect(screen.getByRole('spinbutton', { name: 'Female size ×' })).toHaveValue(1);
  });

  it('a value past the limits is not sent', async () => {
    const { rpc } = await setup();
    const adult = screen.getByRole('spinbutton', { name: 'Adult size ×' });
    await fireEvent.input(adult, { target: { value: '101' } });
    await fireEvent.change(adult);
    expect(rpc.callsTo('mods.setSizes')).toHaveLength(0);
    expect(screen.getByText(/from 0.05 to 100/)).toBeInTheDocument();
  });

  it('the number fields take up to 100; the sliders stay at 5', async () => {
    const { rpc } = await setup();
    expect(screen.getByRole('spinbutton', { name: 'Adult size ×' })).toHaveAttribute('max', '100');
    expect(screen.getByRole('spinbutton', { name: 'Female size ×' })).toHaveAttribute('max', '100');
    expect(screen.getByRole('slider', { name: 'Adult size × slider' })).toHaveAttribute('max', '5');
    const male = screen.getByRole('spinbutton', { name: 'Male size ×' });
    await fireEvent.input(male, { target: { value: '9' } });
    await fireEvent.change(male);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ male: 9 });
  });

  it('names old record edits of the size and removes them', async () => {
    const { rpc } = await setup(sizes({ recordEdits: ['/scaleGrowthCurve/keys/0/value', '/skinsData/1/femaleSizeMultiplier'] }));
    expect(screen.getByText(/\/scaleGrowthCurve\/keys\/0\/value, \/skinsData\/1\/femaleSizeMultiplier/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Remove old size edits' }));
    await waitFor(() => expect(rpc.callsTo('mods.removeSizeEdits')).toHaveLength(1));
    expect(rpc.callsTo('mods.removeSizeEdits')[0].params).toMatchObject({ target: 'Spinosaurus' });
  });

  it('a species without old record edits has no Remove button', async () => {
    await setup();
    expect(screen.queryByRole('button', { name: 'Remove old size edits' })).toBeNull();
  });

  it('a save that fails puts the saved value back', async () => {
    const { rpc } = await setup(sizes({ adult: 1.5 }), [], true);
    const adult = screen.getByRole('spinbutton', { name: 'Adult size ×' });
    await fireEvent.input(adult, { target: { value: '1.2' } });
    await fireEvent.change(adult);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    await waitFor(() => expect(screen.getByRole('spinbutton', { name: 'Adult size ×' })).toHaveValue(1.5));
  });

  it('Reset sends null for that key only', async () => {
    const { rpc } = await setup(sizes({ adult: 1.5, male: 1.2 }));
    await fireEvent.click(screen.getByRole('button', { name: 'Reset Adult size ×' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ target: 'Spinosaurus', adult: null, male: 1.2, baby: null, female: null });
  });

  it('shows the errors Check found for this species', async () => {
    await setup(sizes({ adult: 4 }), [
      'sizes/spinosaurus.json: Spinosaurus would be scaled from 0.04 to 3 (the game allows 0.05 to 2, and clamps it); lower the adult or sex sizes.',
      'sizes/other.json: Other would be scaled badly.',
      'sizes/spinosaurus2.json: Spinosaurus2 would be scaled badly.', // a name that contains this one is another species
      'sizes/x.json: X is not Spinosaurus.',
    ]);
    expect(await screen.findByText(/Spinosaurus would be scaled from 0.04/)).toBeInTheDocument();
    expect(screen.queryByText(/Other would be/)).toBeNull();
    expect(screen.queryByText(/Spinosaurus2 would be/)).toBeNull();
    expect(screen.queryByText(/X is not/)).toBeNull();
  });

  it('dragging a slider moves its number field, the size bar and the lengths live, and saves once on release', async () => {
    const { rpc, container } = await setup(); // 12.1 m long
    const slider = screen.getByRole('slider', { name: 'Adult size × slider' });
    await fireEvent.input(slider, { target: { value: '1.5' } });
    await fireEvent.input(slider, { target: { value: '2' } });
    expect(screen.getByRole('spinbutton', { name: 'Adult size ×' })).toHaveValue(2);
    expect(screen.getByText('12.1 m → 24.2 m')).toBeInTheDocument();
    expect((container.querySelector('.fill.game') as HTMLElement).style.width).toBe('50%'); // the game's half the new size
    expect((container.querySelector('.fill.mod') as HTMLElement).style.width).toBe('100%');
    expect(rpc.callsTo('mods.setSizes')).toHaveLength(0);

    await fireEvent.change(slider);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ adult: 2 });
    expect(screen.getByRole('spinbutton', { name: 'Adult size ×' })).toHaveValue(2);
  });

  it('a set baby share shows 3 decimals and is sent whole', async () => {
    const { rpc } = await setup(sizes({ baby: 0.121014 }));
    expect(screen.getByRole('spinbutton', { name: 'Baby size (share of adult)' })).toHaveValue(0.121);
    const male = screen.getByRole('spinbutton', { name: 'Male size ×' });
    await fireEvent.input(male, { target: { value: '1.1' } });
    await fireEvent.change(male);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ male: 1.1, baby: 0.121014 });
  });

  it('has no size limit row: the limit is automatic', async () => {
    await setup();
    expect(screen.queryByRole('group', { name: 'Size limit' })).toBeNull();
    expect(screen.queryByRole('spinbutton', { name: /Size limit/ })).toBeNull();
    expect(screen.getByText(/The size limit is automatic/)).toBeInTheDocument();
  });

  it('a slider reaches 5, or the saved value when it is higher', async () => {
    await setup(sizes({ adult: 12 }));
    expect(screen.getByRole('slider', { name: 'Adult size × slider' })).toHaveAttribute('max', '12');
    expect(screen.getByRole('slider', { name: 'Male size × slider' })).toHaveAttribute('max', '5');
    expect(screen.getByRole('slider', { name: 'Baby size (share of adult) slider' })).toHaveAttribute('max', '1');
  });

  it('shows the ranges as percentages of this species’ size, with the sizes they give', async () => {
    await setup(sizes({ adult: 2.5, nursery: [0.725, 5], baseName: 'Tyrannosaurus' }));
    const nursery = screen.getByRole('group', { name: 'Nursery size slider range' });
    expect(within(nursery).getByRole('spinbutton', { name: 'Nursery size slider range minimum' })).toHaveValue(72.5);
    expect(within(nursery).getByRole('spinbutton', { name: 'Nursery size slider range maximum' })).toHaveValue(500);
    expect(within(nursery).getByText('→ 1.8× to 12.5× a normal Tyrannosaurus')).toBeInTheDocument();
    expect(within(nursery).getByText('game: 92.5% – 107.5%')).toBeInTheDocument();
    const breeding = screen.getByRole('group', { name: 'Breeding size range' });
    expect(within(breeding).getByRole('spinbutton', { name: 'Breeding size range minimum' })).toHaveValue(85);
    expect(within(breeding).getByRole('spinbutton', { name: 'Breeding size range maximum' })).toHaveValue(115);
    expect(within(breeding).getByText("game's")).toBeInTheDocument();
    expect(screen.getByText(/Random eggs keep the game's range/)).toBeInTheDocument();
  });

  it('a percentage edit sends the multiplier, the other keys as they are', async () => {
    const { rpc } = await setup(sizes({ adult: 1.2 }));
    const max = screen.getByRole('spinbutton', { name: 'Nursery size slider range maximum' });
    await fireEvent.input(max, { target: { value: '300' } });
    await fireEvent.change(max);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    const sent = rpc.callsTo('mods.setSizes')[0].params as Record<string, unknown>;
    expect(sent).toMatchObject({ target: 'Spinosaurus', adult: 1.2, breeding: null });
    expect(sent.nursery).toEqual([0.925, 3]);
    expect(sent).not.toHaveProperty('limit');
  });

  it('a set range shows the game’s beside it, and Reset clears only that key', async () => {
    const { rpc } = await setup(sizes({ breeding: [0.7, 1.3], nursery: [0.5, 3] }));
    const breeding = screen.getByRole('group', { name: 'Breeding size range' });
    expect(within(breeding).getByRole('spinbutton', { name: 'Breeding size range minimum' })).toHaveValue(70);
    expect(within(breeding).getByText('game: 85% – 115%')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Reset Breeding size range' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ breeding: null, nursery: [0.5, 3], adult: null });
  });

  it('does not send a range outside its bounds', async () => {
    const { rpc } = await setup();
    const from = screen.getByRole('spinbutton', { name: 'Breeding size range minimum' });
    expect(from).toHaveAttribute('min', '0.5'); // the smallest it takes, as its check says
    await fireEvent.input(from, { target: { value: '300' } }); // past the maximum (115%)
    await fireEvent.change(from);
    expect(screen.getByRole('alert')).toHaveTextContent('Breeding size range: the minimum must be from 0.5% to the maximum (115%).');
    await fireEvent.input(from, { target: { value: '0' } });
    await fireEvent.change(from);
    expect(screen.getByRole('alert')).toHaveTextContent('Breeding size range: the minimum must be from 0.5% to the maximum (115%).');
    const to = screen.getByRole('spinbutton', { name: 'Nursery size slider range maximum' });
    await fireEvent.input(to, { target: { value: '10100' } });
    await fireEvent.change(to);
    expect(screen.getByRole('alert')).toHaveTextContent('Nursery size slider range: the maximum must be from the minimum (92.5%) to 10000%.');
    expect(rpc.callsTo('mods.setSizes')).toHaveLength(0);
  });

  it('says the smallest hatchling and the biggest adult, following a drag', async () => {
    const { rpc } = await setup(); // the female 1.1 x the bred young's 1.15; a hatchling 0.25 x the smallest bred young
    expect(screen.getByText('Smallest hatchling 0.24×, biggest adult 1.3× a normal Spinosaurus')).toBeInTheDocument();
    const slider = screen.getByRole('slider', { name: 'Adult size × slider' });
    await fireEvent.input(slider, { target: { value: '3' } });
    expect(screen.getByText(/biggest adult 3.8× a normal Spinosaurus/)).toBeInTheDocument(); // 3 x 1.1 x 1.15 = 3.8
    expect(within(screen.getByRole('group', { name: 'Nursery size slider range' })).getByText('→ 2.8× to 3.2× a normal Spinosaurus')).toBeInTheDocument();
    expect(rpc.callsTo('mods.setSizes')).toHaveLength(0);
  });

  it('the number fields show 3 decimals and keep the full value when not edited', async () => {
    const { rpc } = await setup(sizes({ original: { adultLength: 12.1, babyShare: 0.121014, skins: [{ male: 1, female: 1.1 }], curve: [[0, 0.121014], [1, 1]] }, breeding: [0.8512345, 1.15] }));
    expect(screen.getByRole('spinbutton', { name: 'Baby size (share of adult)' })).toHaveValue(0.121);
    expect(screen.getByRole('spinbutton', { name: 'Breeding size range minimum' })).toHaveValue(85.123);
    const adult = screen.getByRole('spinbutton', { name: 'Adult size ×' });
    await fireEvent.input(adult, { target: { value: '1.2' } });
    await fireEvent.change(adult);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ adult: 1.2, baby: null, breeding: [0.8512345, 1.15] });
  });
});
