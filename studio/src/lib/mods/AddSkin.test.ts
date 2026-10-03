import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import AddSkin from './AddSkin.svelte';

const carch = {
  speciesId: 'Carcharodontosaurus', vivarium: false,
  skins: [{ index: 0, name: 'Base', male: true, female: true }, { index: 1, name: 'Alt 1', male: true, female: true }],
};
const modRow = { id: 'red-spot', name: 'Red spot', version: '1.0.0', author: null, replacements: 0, skins: 0, state: 'notInstalled', enabled: null, dir: null, error: null };

function setup(hasDump = true, mods = [modRow]) {
  const list = { mods, frameworkInstalled: false };
  const rpc = new FakeRpc()
    .on('mods.species', () => ({ hasDump, species: hasDump ? [carch] : [] }))
    .on('mods.list', () => list)
    .on('mods.create', () => list)
    .on('mods.addSkin', () => list);
  return { rpc, session: new Session(rpc, new FakePlatform(), memoryStore()) };
}

describe('AddSkin', () => {
  it('explains that a data dump is needed', async () => {
    const { session } = setup(false);
    renderWith(AddSkin, session, { speciesKey: 'carcharodontosaurus' });

    expect(await screen.findByText(/Run data dump/)).toBeInTheDocument();
  });

  it('adds a skin to an existing mod from the chosen base', async () => {
    const { rpc, session } = setup();
    renderWith(AddSkin, session, { speciesKey: 'carcharodontosaurus' });

    await fireEvent.input(await screen.findByRole('textbox', { name: 'Skin name' }), { target: { value: 'Red spot' } });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Base skin' }), { target: { value: 'Alt 1' } });
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Female' })); // male only
    await fireEvent.click(screen.getByRole('button', { name: 'Add skin' }));

    await waitFor(() =>
      expect(rpc.callsTo('mods.addSkin')[0]?.params).toEqual({
        id: 'red-spot', species: 'Carcharodontosaurus', name: 'Red spot', base: 'Alt 1', male: true, female: false, maps: false,
      }),
    );
    expect(session.notice).toContain('Red spot');
  });

  it('creates a new mod first when there is none', async () => {
    const { rpc, session } = setup(true, []);
    renderWith(AddSkin, session, { speciesKey: 'carcharodontosaurus' });

    await fireEvent.input(await screen.findByRole('textbox', { name: 'Skin name' }), { target: { value: 'Red spot' } });
    await fireEvent.input(screen.getByRole('textbox', { name: 'New mod id' }), { target: { value: 'blue-stripes' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Add skin' }));

    await waitFor(() => expect(rpc.callsTo('mods.addSkin')[0]?.params).toMatchObject({ id: 'blue-stripes' }));
    expect(rpc.callsTo('mods.create')[0]?.params).toEqual({ id: 'blue-stripes', name: null, author: null });
  });
});
