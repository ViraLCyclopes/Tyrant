import { fireEvent, screen, waitFor } from '@testing-library/svelte';
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
  target: 'Spinosaurus', file: 'spinosaurus.json', adult: null, baby: null, male: null, female: null,
  original: { adultLength: 12.1, babyShare: 0.25, skins: [{ male: 1, female: 1.1 }] }, finalAdultLength: 12.1, ...over,
});

async function setup(dto: ModSizesDto = sizes(), errors: string[] = [], refuse = false) {
  const rpc = new FakeRpc()
    .on('mods.get', () => modDetail())
    .on('mods.check', () => ({ errors, warnings: [], missingCutouts: [] }))
    .on('mods.sizes', () => dto)
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
    await fireEvent.input(adult, { target: { value: '9' } });
    await fireEvent.change(adult);
    expect(rpc.callsTo('mods.setSizes')).toHaveLength(0);
    expect(screen.getByText(/from 0.05 to 5/)).toBeInTheDocument();
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
});
