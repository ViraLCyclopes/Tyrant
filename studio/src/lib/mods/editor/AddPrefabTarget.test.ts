import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import AddPrefabTarget from './AddPrefabTarget.svelte';

function setup(hasDump = true) {
  const rpc = new FakeRpc()
    .on('mods.species', () => ({
      hasDump,
      species: hasDump ? [
        { speciesId: 'Spinosaurus', vivarium: false, skins: [] },
        { speciesId: 'Allosaurus Anax', vivarium: true, skins: [] },
      ] : [],
    }))
    .on('mods.animalDatabase', () => ({ animals: hasDump ? [{ id: 'a1', key: 'red-spot/dino', base: 'Spinosaurus' }] : [], fields: [] }));
  const session = new Session(rpc, new FakePlatform(), memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  const onDone = vi.fn();
  renderWith(AddPrefabTarget, session, { doc: { id: 'red-spot' }, onDone }, tab);
  return { onDone };
}

describe('AddPrefabTarget', () => {
  it("offers this mod's species and the game's (not vivarium) and opens the pick", async () => {
    const { onDone } = setup();
    expect(await screen.findByRole('heading', { name: 'Add prefab settings' })).toBeInTheDocument();
    await screen.findByRole('option', { name: 'red-spot/dino' });
    expect(screen.queryByRole('option', { name: /Allosaurus Anax/ })).toBeNull();

    await fireEvent.change(screen.getByRole('combobox', { name: 'Species' }), { target: { value: 'Spinosaurus' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Open' }));
    await waitFor(() => expect(onDone).toHaveBeenCalledWith('Spinosaurus'));
  });

  it('Cancel hands back null', async () => {
    const { onDone } = setup();
    await fireEvent.click(await screen.findByRole('button', { name: 'Cancel' }));
    expect(onDone).toHaveBeenCalledWith(null);
  });

  it('says so when there is no data dump', async () => {
    setup(false);
    expect(await screen.findByText(/Run data dump/)).toBeInTheDocument();
  });
});
