import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { CustomSpeciesRow, ModDetail, ModModelRigPreviewDto, SpeciesSkinsRow } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import AddModel from './AddModel.svelte';

/** custom: what mods.customSpecies answers, null when it fails; dumped: rows the data dump has besides the two game species. */
function setup(hasDump = true, custom: CustomSpeciesRow[] | null = [], dumped: SpeciesSkinsRow[] = []) {
  lastPlatform = new FakePlatform();
  const platform = lastPlatform;
  platform.files.push('D:\\blender\\big-carch.glb');
  const rpc = new FakeRpc().on('mods.readModelRig', () => {
    if (rigFails) throw new Error('could not read the model');
    return rigPreview;
  }).on('mods.species', () => ({
    hasDump,
    species: hasDump
      ? [
          { speciesId: 'Allosaurus Anax', vivarium: false, skins: [] },
          { speciesId: 'Carcharodontosaurus', vivarium: false, skins: [] },
          ...dumped,
        ]
      : [],
  })).on('mods.customSpecies', () => {
    if (!custom) throw new Error('could not read the mods');
    return { species: custom };
  });
  lastRpc = rpc;
  const session = new Session(rpc, platform, memoryStore());
  const doc = { id: 'big-carch', detail: { revision: 'r1', models: [] } as unknown as ModDetail, edit: vi.fn(async () => true) };
  const onDone = vi.fn();
  const view = renderWith(AddModel, session, { doc, onDone });
  return { doc, onDone, view, rpc };
}

let lastPlatform: FakePlatform;
let lastRpc: FakeRpc;
let rigFails = false;
let rigPreview: ModModelRigPreviewDto = { source: 'gameSkeleton', bones: [], current: null, question: 'none', changes: false, errors: [], warnings: [], rigFile: null, errorsIfUseFile: [], errorsIfKeep: [] };

describe('AddModel', () => {
  it('lets you pick a .glb or an .fbx', async () => {
    setup();

    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));

    await waitFor(() => expect(lastPlatform.fileDialogs[0]?.extensions).toEqual(['glb', 'fbx']));
    expect(lastPlatform.fileDialogs[0]?.title).toContain('.fbx');
  });

  it("adds the .glb as the chosen species' model in this mod and opens it", async () => {
    const { doc, onDone } = setup();

    const species = await screen.findByRole('combobox', { name: 'Species' });
    await fireEvent.change(species, { target: { value: 'Carcharodontosaurus' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Browse…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add' }));

    await waitFor(() => expect(doc.edit).toHaveBeenCalledWith('mods.replaceModel', { file: 'D:\\blender\\big-carch.glb', target: 'Carcharodontosaurus', rig: 'auto' }));
    await waitFor(() => expect(onDone).toHaveBeenCalledWith('Carcharodontosaurus'));
  });

  it('Add sends the chosen rig choice', async () => {
    rigPreview = { ...rigPreview, source: 'bones', question: 'useOrKeep', changes: true, current: [], bones: [{ bone: 'Jaw', move: [0, 1, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] }] };
    const { doc, onDone } = setup();
    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add' }));
    await fireEvent.click(await screen.findByRole('radio', { name: 'Keep the current one (0 bones)' }));
    expect(doc.edit).not.toHaveBeenCalled();
    await fireEvent.click(screen.getAllByRole('button', { name: 'Add' }).at(-1)!);

    await waitFor(() => expect(doc.edit).toHaveBeenCalledWith('mods.replaceModel', { file: 'D:\\blender\\big-carch.glb', target: 'Allosaurus Anax', rig: 'keepCurrent' }));
    await waitFor(() => expect(onDone).toHaveBeenCalled());
    rigPreview = { ...rigPreview, question: 'none', bones: [], changes: false };
  });

  async function openQuestion() {
    rigPreview = { ...rigPreview, source: 'bones', question: 'useOrKeep', changes: true, current: [], bones: [{ bone: 'Jaw', move: [0, 1, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] }] };
    const made = setup();
    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add' }));
    await screen.findByRole('group', { name: 'Rig edit' });
    return made;
  }

  it('Cancel on the rig question adds nothing', async () => {
    const { doc, onDone } = await openQuestion();
    await fireEvent.click(within(screen.getByRole('group', { name: 'Rig edit' })).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('group', { name: 'Rig edit' })).toBeNull());
    expect(doc.edit).not.toHaveBeenCalled();
    expect(onDone).not.toHaveBeenCalled();
  });

  it('Escape on the rig question adds nothing', async () => {
    const { doc } = await openQuestion();
    await fireEvent.keyDown(window, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('group', { name: 'Rig edit' })).toBeNull());
    expect(doc.edit).not.toHaveBeenCalled();
  });

  it('leaving with the question open calls nothing, and the next card shows no question', async () => {
    const { doc, view } = await openQuestion();
    view.unmount();
    setup();
    await screen.findByRole('button', { name: 'Browse…' });
    expect(screen.queryByRole('group', { name: 'Rig edit' })).toBeNull();
    expect(doc.edit).not.toHaveBeenCalled();
  });

  it('changing the species with the question open adds nothing, and the new species shows no question', async () => {
    const { doc, onDone } = await openQuestion();
    await fireEvent.change(screen.getByRole('combobox', { name: 'Species' }), { target: { value: 'Carcharodontosaurus' } });
    await waitFor(() => expect(screen.queryByRole('group', { name: 'Rig edit' })).toBeNull());
    expect(doc.edit).not.toHaveBeenCalled();
    expect(onDone).not.toHaveBeenCalled();
  });

  it('changing the species while the model is being read asks nothing and adds nothing', async () => {
    const asked = { ...rigPreview, source: 'bones', question: 'useOrKeep', changes: true, current: [], bones: [{ bone: 'Jaw', move: [0, 1, 0], rotate: [0, 0, 0, 1], scale: [1, 1, 1] }] } as ModModelRigPreviewDto;
    const { doc, onDone, rpc } = setup();
    let release = () => {};
    rpc.on('mods.readModelRig', () => new Promise((resolve) => (release = () => resolve(asked))));
    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add' }));
    await waitFor(() => expect(rpc.callsTo('mods.readModelRig')).toHaveLength(1));

    await fireEvent.change(screen.getByRole('combobox', { name: 'Species' }), { target: { value: 'Carcharodontosaurus' } });
    release();
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(screen.queryByRole('group', { name: 'Rig edit' })).toBeNull();
    expect(doc.edit).not.toHaveBeenCalled();
    expect(onDone).not.toHaveBeenCalled();
  });

  it('a model that cannot be read adds nothing', async () => {
    rigFails = true;
    const { doc, onDone, rpc } = setup();
    await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Add' }));
    await waitFor(() => expect(rpc.callsTo('mods.readModelRig')).toHaveLength(1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Add' })).toBeEnabled());
    expect(doc.edit).not.toHaveBeenCalled();
    expect(onDone).not.toHaveBeenCalled();
    rigFails = false;
  });

  describe("this mod's new species", () => {
    const row = (speciesId: string, name: string, own: boolean) => ({ speciesId, name, modId: 'x', modName: 'X', own, inWorkspace: true, installed: false, enabled: false, skins: [] });
    const mine = row('tyrant.big-carch.mirabilis', 'Mirabilis', true);
    const theirs = row('tyrant.other.rex', 'Rex', false);
    const options = () => within(screen.getByRole('combobox', { name: 'Species' })).getAllByRole('option').map((o) => (o as HTMLOptionElement).value);

    beforeEach(() => {
      rigPreview = { ...rigPreview, question: 'none', bones: [], changes: false }; // the rig question's tests above leave it asking
    });

    it("are asked for this mod and listed first, chosen by default; other mods' are not offered", async () => {
      setup(true, [mine, theirs]);

      const species = await screen.findByRole('combobox', { name: 'Species' });
      expect(lastRpc.callsTo('mods.customSpecies')[0]?.params).toEqual({ id: 'big-carch' });
      expect(options()).toEqual(['tyrant.big-carch.mirabilis', 'Allosaurus Anax', 'Carcharodontosaurus']);
      expect(species).toHaveValue('tyrant.big-carch.mirabilis');
      expect(within(species).getByRole('option', { name: 'Mirabilis (this mod)' })).toBeInTheDocument();
    });

    it('can be given a model', async () => {
      const { doc, onDone } = setup(true, [mine]);

      await fireEvent.click(await screen.findByRole('button', { name: 'Browse…' }));
      await waitFor(() => expect(screen.getByRole('textbox', { name: 'Your model' })).toHaveValue('D:\\blender\\big-carch.glb'));
      await fireEvent.click(screen.getByRole('button', { name: 'Add' }));

      await waitFor(() => expect(doc.edit).toHaveBeenCalledWith('mods.replaceModel', { file: 'D:\\blender\\big-carch.glb', target: 'tyrant.big-carch.mirabilis', rig: 'auto' }));
      await waitFor(() => expect(onDone).toHaveBeenCalledWith('tyrant.big-carch.mirabilis'));
    });

    it('are listed once when the data dump has them too (their mod was installed when it ran)', async () => {
      setup(true, [mine], [{ speciesId: 'tyrant.big-carch.mirabilis', vivarium: false, skins: [] }]);

      await screen.findByRole('combobox', { name: 'Species' });
      expect(options().filter((id) => id === 'tyrant.big-carch.mirabilis')).toHaveLength(1);
    });

    it("says what a species model does for the species' skins, only for these species", async () => {
      setup(true, [mine]);
      const species = await screen.findByRole('combobox', { name: 'Species' });
      expect(screen.getByText(/skins with their own model keep it/i)).toBeInTheDocument();

      await fireEvent.change(species, { target: { value: 'Carcharodontosaurus' } });
      expect(screen.queryByText(/skins with their own model keep it/i)).toBeNull();
    });

    it('the game species still work when the list cannot be read', async () => {
      setup(true, null);

      const species = await screen.findByRole('combobox', { name: 'Species' });
      expect(species).toHaveValue('Allosaurus Anax');
    });
  });

  it('waits for a file before adding', async () => {
    setup();

    expect(await screen.findByRole('button', { name: 'Add' })).toBeDisabled();
  });

  it('asks for the data dump when there is none', async () => {
    setup(false);

    expect(await screen.findByText(/Run data dump/)).toBeInTheDocument();
  });
});
