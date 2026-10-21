import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
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
  it('Prefab settings has + Add; the species it picks opens the prefab page', async () => {
    const { rpc } = setup();
    rpc.on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Spinosaurus', vivarium: false, skins: [] }] }))
      .on('mods.animalDatabase', () => ({ animals: [], fields: [] }))
      .on('mods.prefabFields', () => ({ target: 'Spinosaurus', fields: [] }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Add prefab settings' }));
    expect(await screen.findByRole('heading', { name: 'Add prefab settings' })).toBeInTheDocument();
    await screen.findByRole('option', { name: 'Spinosaurus' });
    await fireEvent.click(screen.getByRole('button', { name: 'Open' }));

    expect(await screen.findByRole('heading', { name: /Prefab settings: Spinosaurus/ })).toBeInTheDocument();
  });

  it('Sizes has + Add; the species it picks opens the size page', async () => {
    const { rpc } = setup();
    rpc.on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Spinosaurus', vivarium: false, skins: [] }] }))
      .on('mods.animalDatabase', () => ({ animals: [], fields: [] }))
      .on('mods.sizes', () => ({ target: 'Spinosaurus', file: 'spinosaurus.json', adult: null, baby: null, male: null, female: null, original: { adultLength: 12, babyShare: 0.25, skins: [] }, finalAdultLength: 12, recordEdits: [], limit: [0.05, 2] }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Add sizes' }));
    expect(await screen.findByRole('heading', { name: 'Add sizes' })).toBeInTheDocument();
    await screen.findByRole('option', { name: 'Spinosaurus' });
    await fireEvent.click(screen.getByRole('button', { name: 'Open' }));

    expect(await screen.findByRole('heading', { name: /Sizes: Spinosaurus/ })).toBeInTheDocument();
    expect(await screen.findByRole('spinbutton', { name: 'Adult size ×' })).toBeInTheDocument();
  });

  it('Species materials has + Add; the species it picks opens its Material card, and Remove clears it', async () => {
    const { rpc } = setup(modDetail({ speciesMaterials: [{ species: 'Titanoboa', materialsJson: '{"Titanoboa":{"settings":{"_Smoothness":0.5}}}' }] }));
    rpc.on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Spinosaurus', vivarium: false, skins: [] }] }))
      .on('mods.animalDatabase', () => ({ animals: [], fields: [] }))
      .on('mods.materialInfo', (p) => ({ name: p.species!, animal: p.species!, properties: [], inheritedIridescenceJson: null, gameIridescence: null, gameHasRamp: false, problem: null }))
      .on('mods.setMaterials', () => modDetail({ revision: 'r2' }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Add species materials' }));
    await screen.findByRole('option', { name: 'Spinosaurus' });
    await fireEvent.click(screen.getByRole('button', { name: 'Open' }));
    expect(await screen.findByRole('heading', { name: 'Species materials: Spinosaurus' })).toBeInTheDocument();
    expect(await screen.findByText('Material: Spinosaurus')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Remove' })).toBeDisabled();

    await fireEvent.click(screen.getByRole('button', { name: 'Titanoboa' }));
    expect(await screen.findByRole('heading', { name: 'Species materials: Titanoboa' })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    await waitFor(() => expect(rpc.callsTo('mods.setMaterials')[0]?.params).toMatchObject({ species: 'Titanoboa', materials: null }));
  });

  it('Vanilla skin materials + Add picks a species, then one of its vanilla skins, and opens its Material card', async () => {
    const { rpc } = setup(modDetail());
    rpc.on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Spinosaurus', vivarium: false, skins: [] }] }))
      .on('mods.animalDatabase', () => ({ animals: [], fields: [] }))
      .on('mods.vanillaSkins', () => ({ names: ['Green', 'Sand'] }))
      .on('mods.materialInfo', (p) => ({ name: 'Spinosaurus', animal: 'Spinosaurus', properties: [], inheritedIridescenceJson: null, gameIridescence: null, gameHasRamp: false, problem: p.vanillaSkin ? null : 'wrong' }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Add vanilla skin materials' }));
    await screen.findByRole('option', { name: 'Spinosaurus' });
    await fireEvent.click(screen.getByRole('button', { name: 'Open' }));
    await screen.findByRole('option', { name: 'Sand' });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Vanilla skin' }), { target: { value: 'Sand' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Open' }));
    expect(await screen.findByRole('heading', { name: 'Vanilla skin materials: Spinosaurus / Sand' })).toBeInTheDocument();
    await waitFor(() => expect(rpc.callsTo('mods.materialInfo')[0]?.params).toMatchObject({ vanillaSkin: 'Spinosaurus/Sand' }));
  });

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

  it('a folder of sounds can be for one of the species skins', async () => {
    const { rpc, platform } = setup();
    platform.folders.push('C:\\pack');
    rpc
      .on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Allosaurus Anax', vivarium: false, skins: [{ index: 0, name: 'Ultimasaurus', male: true, female: true }] }] }))
      .on('sounds.forSpecies', () => ({ speciesId: 'Allosaurus Anax', hasEventList: true, sounds: [] }))
      .on('sounds.matchFolder', () => ({
        groups: [{ name: 'AlloAnax_VoxAngry', files: ['C:\\pack\\a.wav'], sounds: [{ event: 'event:/X/Vox/TheroMed_VoxAngry', name: 'Angry', group: 'Calls', species: ['Allosaurus Anax'], lengthMs: null, oneShot: true, perAnimal: true }] }],
        unmatched: [],
      }))
      .on('mods.replaceSounds', () => modDetail({ revision: 'r2' }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Add a sound' }));
    await fireEvent.change(await screen.findByRole('combobox', { name: 'Sounds of' }), { target: { value: 'Allosaurus Anax' } });
    await fireEvent.click(await screen.findByRole('button', { name: 'Replace from folder…' }));
    await fireEvent.change(await screen.findByRole('combobox', { name: 'Who hears them' }), { target: { value: 'Allosaurus Anax/Ultimasaurus' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Replace 1 sound' }));

    await waitFor(() => expect(rpc.callsTo('mods.replaceSounds')[0]?.params).toMatchObject({ species: null, skin: 'Allosaurus Anax/Ultimasaurus' }));
  });

  it("a folder of sounds is replaced in one edit that undo takes back, and the first one opens", async () => {
    const angry = { event: 'event:/X/Vox/TheroMed_VoxAngry', name: 'Angry', group: 'Calls', species: 'Allosaurus Anax', skin: null, files: ['sounds/a.wav', 'sounds/b.wav'], volume: 1, agePitch: 1, chance: null };
    const { rpc, platform, tab } = setup();
    platform.folders.push('C:\pack');
    rpc
      .on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Allosaurus Anax', vivarium: false, skins: [] }] }))
      .on('sounds.forSpecies', () => ({ speciesId: 'Allosaurus Anax', hasEventList: true, sounds: [] }))
      .on('sounds.matchFolder', () => ({
        groups: [{ name: 'AlloAnax_VoxAngry', files: ['C:\pack\a.wav', 'C:\pack\b.wav'], sounds: [{ event: angry.event, name: 'Angry', group: 'Calls', species: ['Allosaurus Anax'], lengthMs: null, oneShot: true, perAnimal: true }] }],
        unmatched: [],
      }))
      .on('mods.replaceSounds', () => modDetail({ revision: 'r2', sounds: [angry] }));

    await fireEvent.click(await screen.findByRole('button', { name: 'Add a sound' }));
    await fireEvent.change(await screen.findByRole('combobox', { name: 'Sounds of' }), { target: { value: 'Allosaurus Anax' } });
    await fireEvent.click(await screen.findByRole('button', { name: 'Replace from folder…' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Replace 1 sound' }));

    expect(await screen.findByRole('heading', { name: 'Sound: Angry' })).toBeInTheDocument();
    expect(rpc.callsTo('mods.replaceSounds')[0]?.params).toMatchObject({
      species: 'Allosaurus Anax', skin: null, sounds: [{ event: angry.event, files: ['C:\pack\a.wav', 'C:\pack\b.wav'] }],
    });
    expect(rpc.callsTo('mods.replaceSounds')[0]?.params).toHaveProperty('revision');
    expect(tab.undo?.canUndo()).toBe(true);
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

  it('Export for sharing asks whether to include the PNGs, then exports', async () => {
    const { tab, rpc, platform } = setup();
    rpc.on('mods.export', () => ({ path: 'D:\\out\\red-spot.zip', warnings: [] }));
    platform.saves.push('D:\\out\\red-spot.zip');
    await screen.findByRole('button', { name: 'Blue-green stripes' });
    const items = tab.menus[0]!.items;
    const item = (typeof items === 'function' ? items() : items).find((i) => 'label' in i && i.label === 'Export for sharing…');

    (item as { run: () => void }).run();
    await fireEvent.click(await screen.findByRole('checkbox', { name: /Include the original PNGs/ }));
    await fireEvent.click(screen.getByRole('button', { name: 'Export…' }));

    await waitFor(() => expect(rpc.callsTo('mods.export')[0]?.params).toMatchObject({ id: 'red-spot', includePngs: true }));
    expect(screen.queryByRole('dialog', { name: /Export/ })).toBeNull();
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

  it('removing the open skin from the side list shows the details page', async () => {
    const detail = modDetail();
    const { rpc } = setup(detail);
    rpc.on('mods.removeSkin', () => modDetail({ revision: 'r2', skins: detail.skins.filter((k) => k.id !== 'blue') }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Blue-green stripes' }));
    expect(await screen.findByRole('heading', { name: 'Files' })).toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Blue-green stripes' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Remove skin…' }));
    await fireEvent.click(within(await screen.findByRole('dialog', { name: 'Remove Blue-green stripes' })).getByRole('button', { name: 'Remove skin' }));

    await waitFor(() => expect(rpc.callsTo('mods.removeSkin')).toHaveLength(1));
    expect(await screen.findByRole('textbox', { name: 'Name' })).toBeInTheDocument(); // Mod details
    expect(screen.queryByRole('heading', { name: 'Files' })).toBeNull();
  });

  it('the install button says Update once the mod in the game is older than the workspace copy', async () => {
    const rpc = new FakeRpc()
      .on('mods.get', () => modDetail())
      .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
      .on('mods.thumbnail', () => ({ file: null }))
      .on('mods.colorPreview', () => ({ files: [] }))
      .on('mods.list', () => ({ mods: [{ id: 'red-spot', name: 'Carch pack', version: '1.0.0', author: null, replacements: 1, skins: 2, state: 'changed', enabled: true, dir: 'D:\ws\mods\red-spot', error: null }], frameworkInstalled: true, frameworkOutdated: false }));
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
    tab.key = 'red-spot';
    renderWith(ModEditor, session, {}, tab);

    expect(await screen.findByRole('button', { name: 'Update in game' })).toBeInTheDocument();
    const items = tab.menus[0]!.items;
    expect((typeof items === 'function' ? items() : items).some((i) => 'label' in i && i.label === 'Update in game')).toBe(true);
  });

  it('Mod → Delete mod… deletes it and closes the tab', async () => {
    const rpc = new FakeRpc()
      .on('mods.get', () => modDetail())
      .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
      .on('mods.thumbnail', () => ({ file: null }))
      .on('mods.colorPreview', () => ({ files: [] }))
      .on('mods.list', () => ({ mods: [{ id: 'red-spot', name: 'Carch pack', version: '1.0.0', author: null, replacements: 1, skins: 2, state: 'notInstalled', enabled: null, dir: 'D:\ws\mods\red-spot', error: null }], frameworkInstalled: false, frameworkOutdated: false }))
      .on('mods.delete', () => ({ mods: [], frameworkInstalled: false, frameworkOutdated: false }));
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const closed: [string, string][] = [];
    const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null, closeTool: (t, k) => void closed.push([t, k]) });
    tab.key = 'red-spot';
    tab.active = true;
    renderWith(ModEditor, session, {}, tab);
    await screen.findByRole('button', { name: 'Blue-green stripes' });

    const items = tab.menus[0]!.items;
    const del = (typeof items === 'function' ? items() : items).find((i) => 'label' in i && i.label === 'Delete mod…');
    if (!del || !('run' in del)) throw new Error('no Delete mod… item');
    del.run();
    const dialog = await screen.findByRole('dialog', { name: 'Delete Carch pack' });
    expect(within(dialog).queryByRole('checkbox')).toBeNull(); // not installed
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Delete mod' }));

    await waitFor(() => expect(rpc.callsTo('mods.delete')[0]?.params).toEqual({ id: 'red-spot', removeFromGame: false }));
    expect(closed).toEqual([['mod', 'red-spot']]);
  });
});
