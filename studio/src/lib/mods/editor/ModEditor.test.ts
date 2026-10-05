import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { messages, renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import ModEditor from './ModEditor.svelte';

function setup(detail = modDetail()) {
  const rpc = new FakeRpc()
    .on('mods.get', () => detail)
    .on('mods.check', () => ({ errors: [], warnings: ['red-spot/blue male diffuse: small'], missingCutouts: [] }))
    .on('mods.thumbnail', () => ({ file: null }))
    .on('mods.colorPreview', () => ({ files: [] }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const titles: string[] = [];
  const tab = new Tab('tab-mod', session.log, { retitle: (_id, t) => void titles.push(t), openTool: () => null });
  tab.key = 'red-spot';
  tab.active = true;
  renderWith(ModEditor, session, {}, tab);
  return { rpc, platform, session, tab, titles };
}

describe('ModEditor', () => {
  it('lists species models under Models and opens their page', async () => {
    setup(modDetail({ models: [{ target: 'Carcharodontosaurus', skin: null, file: 'models/carch-1a2b.glb', lods: [], errors: [], warnings: [], stale: false, origin: null, originChanged: false }] }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Carcharodontosaurus' }));

    expect(await screen.findByRole('heading', { name: 'Model: Carcharodontosaurus' })).toBeInTheDocument();
  });

  it('Models and Sounds each have + Add, which opens its form', async () => {
    const { rpc } = setup();
    rpc.on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Carcharodontosaurus', vivarium: false, skins: [] }] }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Add a model' }));
    expect(await screen.findByRole('heading', { name: 'Add a model' })).toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Add a sound' }));
    expect(await screen.findByRole('heading', { name: 'Add a sound' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Add a model' })).toBeNull();
  });

  it('Texture replacements has + Add, and the replacement it adds is opened', async () => {
    const { rpc, platform } = setup();
    rpc.on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Carcharodontosaurus', vivarium: false, skins: [] }] }))
      .on('mods.speciesTextures', () => ({ speciesId: 'Carcharodontosaurus', textures: [{ texture: 'T_carch_new_D', slot: 'adult colour', skins: ['Base'], sharedWith: [] }] }))
      .on('mods.replace', () => ({ mods: [] }));
    platform.files.push('D:\\paint\\new_D.png');

    await fireEvent.click(await screen.findByRole('button', { name: 'Add a texture replacement' }));
    expect(await screen.findByRole('heading', { name: 'Replace a texture' })).toBeInTheDocument();
    await fireEvent.change(screen.getByRole('combobox', { name: 'Textures of' }), { target: { value: 'Carcharodontosaurus' } });
    rpc.on('mods.get', () => modDetail({ replace: [{ texture: 'T_carch_new_D', key: null, guid: null, file: 'textures/T_carch_new_D.png' }] }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Replace T_carch_new_D' }));

    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Replace a texture' })).toBeNull());
    expect(await screen.findByRole('button', { name: 'T_carch_new_D' })).toHaveClass('on');
  });

  it('a sound added from + Add is reloaded and opened', async () => {
    const added = { event: 'event:/X/Vox/TheroLarge_VoxSocialCall', name: 'Social call', group: 'Calls', species: 'Carcharodontosaurus', skin: null, files: ['sounds/a.wav'], volume: 1, agePitch: 1, chance: null };
    const { rpc, platform } = setup();
    rpc.on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Carcharodontosaurus', vivarium: false, skins: [] }] }))
      .on('sounds.forSpecies', () => ({ speciesId: 'Carcharodontosaurus', hasEventList: true, sounds: [{ event: added.event, name: 'Social call', group: 'Calls', species: ['Carcharodontosaurus'], lengthMs: null, oneShot: true, perAnimal: true }] }))
      .on('mods.replaceSound', () => modDetail({ sounds: [added] }));
    platform.fileLists.push(['D:\\call.ogg']);

    await fireEvent.click(await screen.findByRole('button', { name: 'Add a sound' }));
    await fireEvent.change(await screen.findByRole('combobox', { name: 'Sounds of' }), { target: { value: 'Carcharodontosaurus' } });
    await fireEvent.click(await screen.findByRole('button', { name: 'Replace Social call' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Choose files…' }));
    rpc.on('mods.get', () => modDetail({ sounds: [added] })); // the editor reloads the mod after adding
    await fireEvent.click(await screen.findByRole('button', { name: 'Add to mod' }));

    expect(await screen.findByRole('heading', { name: 'Sound: Social call' })).toBeInTheDocument();
  });

  it('lists sounds under Sounds with who hears them and opens their page', async () => {
    setup(modDetail({
      sounds: [
        { event: 'event:/X/Vox/TheroLarge_VoxSocialCall', name: 'Social call', group: 'Calls', species: 'Carcharodontosaurus', skin: null, files: ['sounds/a.wav'], volume: 1, agePitch: 1, chance: null },
        { event: 'event:/User Interface/UI_Click', name: 'Click', group: 'Interface', species: null, skin: null, files: ['sounds/b.ogg'], volume: 1, agePitch: 1, chance: null },
      ],
    }));

    expect(await screen.findByText('Sounds (2)')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Click — everyone' })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Social call — Carcharodontosaurus' }));

    expect(await screen.findByRole('heading', { name: 'Sound: Social call' })).toBeInTheDocument();
  });

  it('lists the mod, its skins and replacements, and names the tab after the mod', async () => {
    const { titles } = setup();
    expect(await screen.findByRole('button', { name: 'Blue-green stripes' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Red spot' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'T_carch_alt1_male_D' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: /^Check/ })).toHaveTextContent('1'));
    expect(titles.at(-1)).toBe('Carch pack');
  });

  it('editing the details saves them with the revision', async () => {
    const { rpc } = setup();
    rpc.on('mods.setDetails', (p) => modDetail({ name: p.name, revision: 'r2' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Mod details' }));
    const name = screen.getByRole('textbox', { name: 'Name' });
    await fireEvent.input(name, { target: { value: 'Carch pack 2' } });
    await fireEvent.change(name);
    await waitFor(() => expect(rpc.callsTo('mods.setDetails')[0]?.params).toMatchObject({ id: 'red-spot', revision: 'r1', name: 'Carch pack 2', version: '1.0.0' }));
  });

  it('removing a texture replacement asks first', async () => {
    const { rpc, platform } = setup();
    rpc.on('mods.removeReplacement', () => modDetail({ replace: [], revision: 'r2' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'T_carch_alt1_male_D' }));
    platform.confirmAnswer = false;
    await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    expect(rpc.callsTo('mods.removeReplacement')).toHaveLength(0);
    platform.confirmAnswer = true;
    await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    await waitFor(() => expect(rpc.callsTo('mods.removeReplacement')[0]?.params).toMatchObject({ texture: 'T_carch_alt1_male_D' }));
  });

  it('the Mod menu can export the mod for sharing', async () => {
    const { tab } = setup();
    await screen.findByRole('button', { name: 'Blue-green stripes' });

    const items = tab.menus[0]!.items;
    expect((typeof items === 'function' ? items() : items).map((i) => ('label' in i ? i.label : ''))).toContain('Export for sharing…');
  });

  it('adds a Mod menu and an undo target to its tab', async () => {
    const { tab } = setup();
    await screen.findByRole('button', { name: 'Blue-green stripes' });
    expect(tab.menus.map((m) => m.label)).toEqual(['Mod']);
    expect(tab.undo?.canUndo()).toBe(false);
  });

  it('a failed load explains itself', async () => {
    const rpc = new FakeRpc().on('mods.get', () => {
      throw new Error("There is no mod 'red-spot' in this workspace.");
    });
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
    tab.key = 'red-spot';
    renderWith(ModEditor, session, {}, tab);
    expect(await screen.findByText(/could not be opened/)).toBeInTheDocument();
    expect(messages(session, tab).join('\n')).toContain('no mod');
  });
});
