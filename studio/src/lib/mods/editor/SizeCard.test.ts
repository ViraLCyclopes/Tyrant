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
  nursery: null, breeding: null, limit: null, gameNursery: [0.925, 1.075], gameBreeding: [0.85, 1.15], gameLimit: [0.05, 2],
  original: { adultLength: 12.1, babyShare: 0.25, skins: [{ male: 1, female: 1.1 }] }, finalAdultLength: 12.1, ...over,
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
  renderWith(SizeCard, session, { doc, target: 'Spinosaurus' }, tab);
  await screen.findByRole('spinbutton', { name: 'Adult size ×' });
  return { rpc, doc };
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
    await fireEvent.input(adult, { target: { value: '21' } });
    await fireEvent.change(adult);
    expect(rpc.callsTo('mods.setSizes')).toHaveLength(0);
    expect(screen.getByText(/from 0.05 to 20/)).toBeInTheDocument();
  });

  it('the number fields take up to 20 under the game’s limit; the sliders stay at 5', async () => {
    const { rpc } = await setup(); // the game's limit, 0.05 to 2
    expect(screen.getByRole('spinbutton', { name: 'Adult size ×' })).toHaveAttribute('max', '20');
    expect(screen.getByRole('spinbutton', { name: 'Female size ×' })).toHaveAttribute('max', '20');
    expect(screen.getByRole('slider', { name: 'Adult size × slider' })).toHaveAttribute('max', '5');
    const male = screen.getByRole('spinbutton', { name: 'Male size ×' });
    await fireEvent.input(male, { target: { value: '9' } });
    await fireEvent.change(male);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ male: 9 });
  });

  it('with a raised size limit the sliders reach it', async () => {
    // The species' own limit, from its Size card.
    const { rpc } = await setup(sizes({ limit: [0.05, 20] }));
    expect(screen.getByRole('slider', { name: 'Adult size × slider' })).toHaveAttribute('max', '20');
    expect(screen.getByRole('slider', { name: 'Male size × slider' })).toHaveAttribute('max', '20');
    expect(screen.getByRole('slider', { name: 'Female size × slider' })).toHaveAttribute('max', '20');
    expect(screen.getByRole('slider', { name: 'Baby size (share of adult) slider' })).toHaveAttribute('max', '1');
    const adult = screen.getByRole('spinbutton', { name: 'Adult size ×' });
    await fireEvent.input(adult, { target: { value: '21' } });
    await fireEvent.change(adult);
    expect(screen.getByText(/from 0.05 to 20/)).toBeInTheDocument();
    await fireEvent.input(adult, { target: { value: '12' } });
    await fireEvent.change(adult);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ adult: 12 });
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

  it('shows the game’s ranges and limit pre-filled, marked as the game’s', async () => {
    await setup();
    const nursery = screen.getByRole('group', { name: 'Nursery size slider range' });
    expect(within(nursery).getByRole('spinbutton', { name: 'Nursery size slider range minimum' })).toHaveValue(0.925);
    expect(within(nursery).getByRole('spinbutton', { name: 'Nursery size slider range maximum' })).toHaveValue(1.075);
    expect(within(nursery).getByText("game's")).toBeInTheDocument();
    const breeding = screen.getByRole('group', { name: 'Breeding size range' });
    expect(within(breeding).getByRole('spinbutton', { name: 'Breeding size range minimum' })).toHaveValue(0.85);
    expect(within(breeding).getByRole('spinbutton', { name: 'Breeding size range maximum' })).toHaveValue(1.15);
    const limit = screen.getByRole('group', { name: 'Size limit' });
    expect(within(limit).getByRole('spinbutton', { name: 'Size limit minimum' })).toHaveValue(0.05);
    expect(within(limit).getByRole('spinbutton', { name: 'Size limit maximum' })).toHaveValue(2);
    expect(within(limit).getByText("game's")).toBeInTheDocument();
    expect(screen.getByText(/Random eggs keep the game's range/)).toBeInTheDocument();
    expect(screen.getByText('The game caps animals at 0.05×–2×. A wider limit lets sizes past 2× show in game; the game was tuned for normal sizes, so very big animals may clip, get stuck or frame badly.')).toBeInTheDocument();
    expect(screen.getByText('Minimum 0.01–0.05, maximum 2–20. With several mods on, the widest limit wins.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Reset/ })).toBeNull();
  });

  it('editing a range end sends that key from the game’s values, the other keys as they are', async () => {
    const { rpc } = await setup(sizes({ adult: 1.2 }));
    const max = screen.getByRole('spinbutton', { name: 'Nursery size slider range maximum' });
    await fireEvent.input(max, { target: { value: '3' } });
    await fireEvent.change(max);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ target: 'Spinosaurus', adult: 1.2, nursery: [0.925, 3], breeding: null, limit: null });
  });

  it('a set range shows its values and the game’s, and Reset clears only that key', async () => {
    const { rpc } = await setup(sizes({ breeding: [0.7, 1.3], limit: [0.05, 20] }));
    const breeding = screen.getByRole('group', { name: 'Breeding size range' });
    expect(within(breeding).getByRole('spinbutton', { name: 'Breeding size range minimum' })).toHaveValue(0.7);
    expect(within(breeding).getByText('game: 0.85 – 1.15')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Reset Size limit' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ limit: null, breeding: [0.7, 1.3], nursery: null, adult: null });
  });

  it('sets each end of the size limit within its bounds', async () => {
    const { rpc } = await setup();
    const max = screen.getByRole('spinbutton', { name: 'Size limit maximum' });
    expect(max).toHaveAttribute('min', '2');
    expect(max).toHaveAttribute('max', '20');
    await fireEvent.input(max, { target: { value: '20' } });
    await fireEvent.change(max);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ limit: [0.05, 20] });
  });

  it('sets the size limit minimum with its maximum kept', async () => {
    const { rpc } = await setup(sizes({ limit: [0.05, 20] }));
    const min = screen.getByRole('spinbutton', { name: 'Size limit minimum' });
    expect(min).toHaveAttribute('min', '0.01');
    expect(min).toHaveAttribute('max', '0.05');
    await fireEvent.input(min, { target: { value: '0.02' } });
    await fireEvent.change(min);
    await waitFor(() => expect(rpc.callsTo('mods.setSizes')).toHaveLength(1));
    expect(rpc.callsTo('mods.setSizes')[0].params).toMatchObject({ limit: [0.02, 20] });
  });

  it('does not send a limit or a range outside its bounds', async () => {
    const { rpc } = await setup();
    const min = screen.getByRole('spinbutton', { name: 'Size limit minimum' });
    await fireEvent.input(min, { target: { value: '1' } });
    await fireEvent.change(min);
    expect(screen.getByRole('alert')).toHaveTextContent('Size limit: the minimum must be from 0.01 to 0.05.');
    const max = screen.getByRole('spinbutton', { name: 'Size limit maximum' });
    await fireEvent.input(max, { target: { value: '25' } });
    await fireEvent.change(max);
    expect(screen.getByRole('alert')).toHaveTextContent('Size limit: the maximum must be from 2 to 20.');
    const from = screen.getByRole('spinbutton', { name: 'Breeding size range minimum' });
    await fireEvent.input(from, { target: { value: '3' } }); // past the maximum (1.15)
    await fireEvent.change(from);
    expect(screen.getByRole('alert')).toHaveTextContent('Breeding size range: the minimum must be above 0 and at most the maximum (1.15).');
    const to = screen.getByRole('spinbutton', { name: 'Nursery size slider range maximum' });
    await fireEvent.input(to, { target: { value: '21' } });
    await fireEvent.change(to);
    expect(screen.getByRole('alert')).toHaveTextContent('Nursery size slider range: the maximum must be from the minimum (0.925) to 20.');
    expect(rpc.callsTo('mods.setSizes')).toHaveLength(0);
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
});
