import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import SpeciesDatabase from './SpeciesDatabase.svelte';

const animal = { id: 'new-animal', key: 'tyrant.red-spot.new-animal', base: 'Allosaurus Anax', name: 'New animal', genus: 'Example', epithet: 'animal', cost: null, stats: { adultMassKg: 4500 }, inheritedCost: 20000, inheritedStats: { adultMassKg: 4010, maxAgeYears: 40 }, dataFields: [{ path: '/brain/needs/adult/native/hitPoints', kind: 'number', value: '660', templateValue: '660', editable: true }], localization: { description: 'pack.description' }, icon: null as string | null, paleopediaPicture: null as string | null,
  texts: languageTexts({ 'en-US': { name: 'New animal', description: 'English description.', characterNotes: '', socialInfo: '' } }) };

/** Every language's texts (all empty unless given), as mods.animalDatabase lists them. */
function languageTexts(given: Record<string, Record<string, string>>): Record<string, Record<string, string>> {
  const empty = { name: '', description: '', characterNotes: '', socialInfo: '' };
  return Object.fromEntries(['en-US', 'fr', 'es', 'pt', 'de', 'ru', 'zh-CN'].map((code) => [code, { ...empty, ...given[code] }]));
}
const fields = [{ id: 'adultMassKg', label: 'Adult mass', unit: 'kg', min: 0.001, max: 1000000 }, { id: 'maxAgeYears', label: 'Lifespan', unit: 'years', min: 0.001, max: 10000 }];

async function setup(animals: (typeof animal)[] = [animal], props: { onPrefab?: (t: string) => void; onAddSkin?: (k: string) => void; onOpenSkin?: (id: string) => void; onAddSound?: (k: string) => void; onOpenSound?: (s: unknown) => void } = {}, templateFound = true, detail = modDetail()) {
  const rpc = new FakeRpc()
    .on('mods.get', () => detail)
    .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
    .on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Allosaurus Anax', vivarium: false, skins: [{ index: 0, name: 'Default', male: true, female: true }, { index: 1, name: 'Albino', male: true, female: true }], prefabGuid: null },
      { speciesId: 'Carcharodontosaurus', vivarium: false, skins: [{ index: 0, name: 'Base', male: true, female: true }, { index: 1, name: 'Alt 1', male: true, female: true }, { index: 2, name: 'Alt 2', male: true, female: true }], prefabGuid: null }] }))
    .on('mods.setSpeciesStartingSkin', () => ({ ...detail, revision: 'r2' }))
    .on('mods.setSpeciesSwatch', () => ({ ...detail, revision: 'r3' }))
    .on('mods.animalDatabase', () => ({ animals, fields }))
    .on('mods.thumbnail', (p) => ({ file: `D:/previews/${String(p.file).split('/').pop()}` }))
    .on('mods.speciesTemplate', (p) => (templateFound ? { template: p.template, found: true, cost: 20000, stats: { adultMassKg: 4010, maxAgeYears: 40 } } : { template: p.template, found: false, cost: null, stats: {} }))
    .on('blender.status', () => ({ found: true, exe: 'b.exe', version: '5.2.2', supported: true, missingConfigured: null, addon: 'current', addonInstalled: '0.1.0', addonBundled: '0.1.0', problem: null }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  renderWith(SpeciesDatabase, session, { doc, ...props }, tab);
  await screen.findByRole('spinbutton', { name: /Adult mass/ });
  return { rpc, platform, doc };
}

describe('Species database', () => {
  it("a new species shows its template's cost and stats before it is created", async () => {
    const { rpc } = await setup([]);
    await waitFor(() => expect(screen.getByRole('spinbutton', { name: /Adult mass/ })).toHaveAttribute('placeholder', '4010'));
    expect(rpc.callsTo('mods.speciesTemplate')[0]?.params).toEqual({ template: 'Allosaurus Anax' });
    expect(screen.getByText('Template: 4010')).toBeInTheDocument();
    expect(screen.getByRole('spinbutton', { name: /Lifespan/ })).toHaveAttribute('placeholder', '40');
    expect(screen.getByRole('spinbutton', { name: /Purchase cost/ })).toHaveAttribute('placeholder', '20000');
  });
  it('a template the dump does not have keeps the plain placeholders', async () => {
    const { rpc } = await setup([], {}, false);
    await waitFor(() => expect(rpc.callsTo('mods.speciesTemplate').length).toBeGreaterThan(0));
    expect(screen.getByRole('spinbutton', { name: /Adult mass/ })).toHaveAttribute('placeholder', 'Template value');
    expect(screen.getByRole('spinbutton', { name: /Purchase cost/ })).toHaveAttribute('placeholder', 'Template value');
    expect(screen.queryByText(/^Template: /)).toBeNull();
  });
  it('a species has Prefab settings and Open in Blender, and no Unity buttons', async () => {
    const onPrefab = vi.fn();
    await setup([{ ...animal, key: 'tyrant.red-spot.mirabilis' }], { onPrefab });
    await fireEvent.click(await screen.findByRole('button', { name: 'Prefab settings' }));
    expect(onPrefab).toHaveBeenCalledWith('tyrant.red-spot.mirabilis');
    expect(screen.getByRole('button', { name: /Open in Blender/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Unity/ })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Use template prefab' })).toBeNull();
  });
  it('offers Use template prefab only when the species has a bundle', async () => {
    const { rpc } = await setup([{ ...animal, prefab: { bundle: 'C:/x/m.bundle', asset: 'assets/m.prefab' } } as typeof animal]);
    rpc.on('mods.setSpeciesPrefab', () => modDetail({ revision: 'r2' }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Use template prefab' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesPrefab')[0]?.params).toMatchObject({ speciesId: 'new-animal', bundle: null, asset: null }));
  });
  it('saves full fields and localization keys and imports a custom icon', async () => {
    const { rpc, platform } = await setup();
    rpc.on('mods.setSpecies', () => modDetail({ revision: 'r2' }));
    expect(screen.queryByRole('textbox', { name: '/brain/needs/adult/native/hitPoints' })).toBeNull();
    for (const folder of ['brain', 'needs', 'adult', 'native']) await fireEvent.click(screen.getByRole('button', { name: new RegExp(`^${folder} `) }));
    await fireEvent.input(screen.getByRole('textbox', { name: '/brain/needs/adult/native/hitPoints' }), { target: { value: '800' } });
    await fireEvent.input(screen.getByRole('textbox', { name: 'description key' }), { target: { value: 'pack.mirabilis.description' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save species' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpecies')[0]?.params).toMatchObject({ dataFields: { '/brain/needs/adult/native/hitPoints': '800' }, localization: { description: 'pack.mirabilis.description' } }));
    rpc.on('mods.setSpeciesIcon', () => modDetail({ revision: 'r3' }));
    platform.files.push('C:/icons/mirabilis.png');
    expect(screen.getByRole('heading', { name: 'Nursery picture' })).toBeInTheDocument();
    expect(screen.getByText(/its Nursery card and the Nursery's species panel/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Nursery picture: Choose PNG…' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesIcon')[0]?.params).toMatchObject({ speciesId: 'new-animal', png: 'C:/icons/mirabilis.png' }));
  });
  it('Paleopedia text shows the English texts and saves only the ones changed', async () => {
    const { rpc } = await setup();
    rpc.on('mods.setSpecies', () => modDetail({ revision: 'r2' }));
    const section = screen.getByRole('region', { name: 'Paleopedia text' });
    const description = within(section).getByRole('textbox', { name: 'Description' });
    expect(description).toHaveValue('English description.');
    expect(within(section).queryByRole('textbox', { name: 'Name' })).toBeNull();
    expect(within(section).getByText(/English name is the Display name/)).toBeInTheDocument();
    expect(within(section).getByText(/Every language without its own text uses the English/)).toBeInTheDocument();

    await fireEvent.input(within(section).getByRole('textbox', { name: 'Character notes' }), { target: { value: 'Grumpy.' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save species' }));

    await waitFor(() => expect((rpc.callsTo('mods.setSpecies')[0]?.params as { texts?: unknown } | undefined)?.texts).toEqual({ 'en-US': { characterNotes: 'Grumpy.' } }));
  });

  it('another language uses English until it has its own text, and Use English empties it again', async () => {
    const translated = { ...animal, texts: languageTexts({ 'en-US': { name: 'New animal', description: 'English description.' }, fr: { socialInfo: 'Vit seul.' } }) };
    const { rpc } = await setup([translated]);
    rpc.on('mods.setSpecies', () => modDetail({ revision: 'r2' }));
    const section = screen.getByRole('region', { name: 'Paleopedia text' });
    await fireEvent.change(within(section).getByRole('combobox', { name: 'Language' }), { target: { value: 'fr' } });

    const description = within(section).getByRole('textbox', { name: 'Description' });
    expect(description).toHaveValue('');
    expect(description).toHaveAttribute('placeholder', 'English description.');
    expect(within(section).getByRole('textbox', { name: 'Name' })).toHaveAttribute('placeholder', 'New animal');
    expect(within(section).getAllByText('Uses English').length).toBe(3);
    expect(within(section).getByRole('textbox', { name: 'Social info' })).toHaveValue('Vit seul.');

    await fireEvent.input(description, { target: { value: 'Grand.' } });
    await fireEvent.input(within(section).getByRole('textbox', { name: 'Name' }), { target: { value: 'Nouvel animal' } });
    await fireEvent.click(within(section).getByRole('button', { name: 'Social info: Use English' }));
    expect(within(section).getByRole('textbox', { name: 'Social info' })).toHaveValue('');
    await fireEvent.click(screen.getByRole('button', { name: 'Save species' }));

    await waitFor(() => expect((rpc.callsTo('mods.setSpecies')[0]?.params as { texts?: unknown } | undefined)?.texts).toEqual({ fr: { description: 'Grand.', name: 'Nouvel animal', socialInfo: '' } }));
  });

  it('typed text survives a reload from another change on the page, and the save still sends it', async () => {
    let revision = 1;
    const { rpc, platform } = await setup();
    rpc.on('mods.setSpeciesIcon', () => modDetail({ revision: `r${++revision}` }));
    rpc.on('mods.setSpecies', () => modDetail({ revision: `r${++revision}` }));
    const section = screen.getByRole('region', { name: 'Paleopedia text' });
    await fireEvent.input(within(section).getByRole('textbox', { name: 'Social info' }), { target: { value: 'Lives in packs.' } });

    platform.files.push('C:/icons/mirabilis.png');
    await fireEvent.click(screen.getByRole('button', { name: 'Nursery picture: Choose PNG…' }));
    await waitFor(() => expect(rpc.callsTo('mods.animalDatabase').length).toBeGreaterThan(1));

    expect(within(section).getByRole('textbox', { name: 'Social info' })).toHaveValue('Lives in packs.');
    expect(within(section).getByRole('textbox', { name: 'Description' })).toHaveValue('English description.');
    await fireEvent.click(screen.getByRole('button', { name: 'Save species' }));
    await waitFor(() => expect((rpc.callsTo('mods.setSpecies')[0]?.params as { texts?: unknown } | undefined)?.texts).toEqual({ 'en-US': { socialInfo: 'Lives in packs.' } }));
  });

  it('another species starts from its own texts, not what was typed on the last one', async () => {
    const other = { ...animal, id: 'other-animal', key: 'tyrant.red-spot.other-animal', name: 'Other animal', texts: languageTexts({ 'en-US': { description: 'Other description.' } }) };
    await setup([animal, other]);
    const section = () => screen.getByRole('region', { name: 'Paleopedia text' });
    await fireEvent.input(within(section()).getByRole('textbox', { name: 'Description' }), { target: { value: 'Typed.' } });

    await fireEvent.change(screen.getByRole('combobox', { name: 'Animal' }), { target: { value: 'other-animal' } });
    await waitFor(() => expect(within(section()).getByRole('textbox', { name: 'Description' })).toHaveValue('Other description.'));
    await fireEvent.change(screen.getByRole('combobox', { name: 'Animal' }), { target: { value: 'new-animal' } });
    await waitFor(() => expect(within(section()).getByRole('textbox', { name: 'Description' })).toHaveValue('English description.'));
  });

  it('renaming the species saves the Display name and never sends the English name text', async () => {
    const { rpc } = await setup();
    rpc.on('mods.setSpecies', () => modDetail({ revision: 'r2' }));
    await fireEvent.input(screen.getByRole('textbox', { name: 'Display name' }), { target: { value: 'Ultimasaurus' } });
    await fireEvent.input(within(screen.getByRole('region', { name: 'Paleopedia text' })).getByRole('textbox', { name: 'Description' }), { target: { value: 'The biggest.' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save species' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpecies')[0]?.params).toMatchObject({ name: 'Ultimasaurus' }));
    expect((rpc.callsTo('mods.setSpecies')[0]?.params as { texts?: unknown }).texts).toEqual({ 'en-US': { description: 'The biggest.' } });
  });

  it('a blank translation counts as using English', async () => {
    await setup([{ ...animal, texts: languageTexts({ 'en-US': { description: 'English description.' }, fr: { description: '  ' } }) }]);
    const section = screen.getByRole('region', { name: 'Paleopedia text' });
    expect(within(within(section).getByRole('combobox', { name: 'Language' })).getByRole('option', { name: 'French (uses English)' })).toBeInTheDocument();
    await fireEvent.change(within(section).getByRole('combobox', { name: 'Language' }), { target: { value: 'fr' } });
    expect(within(section).getAllByText('Uses English').length).toBe(4);
  });

  it('the language picker marks the languages that have their own text', async () => {
    await setup([{ ...animal, texts: languageTexts({ 'en-US': { description: 'English description.' }, de: { description: 'Groß.' } }) }]);
    const picker = within(screen.getByRole('region', { name: 'Paleopedia text' })).getByRole('combobox', { name: 'Language' });
    expect(within(picker).getByRole('option', { name: 'German (translated)' })).toBeInTheDocument();
    expect(within(picker).getByRole('option', { name: 'French (uses English)' })).toBeInTheDocument();
    expect(within(picker).getByRole('option', { name: 'English' })).toBeInTheDocument();
  });

  it('previews the chosen Nursery and Paleopedia pictures, wide and uncropped', async () => {
    const { rpc } = await setup([{ ...animal, icon: 'icons/new-animal.png', paleopediaPicture: 'icons/new-animal-paleopedia.png' }]);

    const nursery = await screen.findByRole('img', { name: 'Nursery picture of New animal' });
    const paleopedia = await screen.findByRole('img', { name: 'Paleopedia picture of New animal' });
    await waitFor(() => expect(nursery).toHaveAttribute('src', expect.stringContaining('new-animal.png')));
    expect(rpc.callsTo('mods.thumbnail').map((c) => c.params)).toEqual(expect.arrayContaining([
      expect.objectContaining({ file: 'icons/new-animal.png' }), expect.objectContaining({ file: 'icons/new-animal-paleopedia.png' })]));
    expect(nursery.closest('.thumb')).toHaveStyle({ width: '256px', height: '128px' });
    expect(paleopedia.closest('.thumb')).toHaveStyle({ width: '256px', height: '144px' });
    expect(nursery.closest('.thumb')).toHaveClass('whole');
  });

  it('shows no preview while a species uses its template pictures', async () => {
    await setup();
    expect(screen.queryByRole('img', { name: /picture of New animal/ })).toBeNull();
  });

  it('sets the Paleopedia picture apart from the Nursery picture, and goes back to the template one', async () => {
    const { rpc, platform } = await setup([{ ...animal, paleopediaPicture: 'icons/new-animal-paleopedia.png' }]);
    rpc.on('mods.setSpeciesPaleopediaPicture', () => modDetail({ revision: 'r2' }));
    expect(screen.getByRole('heading', { name: 'Paleopedia picture' })).toBeInTheDocument();
    expect(screen.getByText(/Using icons\/new-animal-paleopedia\.png\./)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Nursery picture: Use template picture' })).toBeDisabled();
    platform.files.push('C:/art/page.png');
    await fireEvent.click(screen.getByRole('button', { name: 'Paleopedia picture: Choose PNG…' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesPaleopediaPicture')[0]?.params).toMatchObject({ speciesId: 'new-animal', png: 'C:/art/page.png' }));
    expect(rpc.callsTo('mods.setSpeciesIcon')).toHaveLength(0);
    await fireEvent.click(screen.getByRole('button', { name: 'Paleopedia picture: Use template picture' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesPaleopediaPicture')[1]?.params).toMatchObject({ speciesId: 'new-animal', png: null }));
  });
  it('saves overrides and can reset them to inherited values', async () => {
    const { rpc, doc } = await setup();
    rpc.on('mods.setSpecies', () => modDetail({ revision: 'r2' }));
    expect(screen.getByRole('spinbutton', { name: /Adult mass/ })).toHaveValue(4500);
    expect(screen.getByRole('spinbutton', { name: /Lifespan/ })).toHaveAttribute('placeholder', '40');
    await fireEvent.click(screen.getByRole('button', { name: 'Reset stats to template' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Save species' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpecies')[0]?.params).toMatchObject({ speciesId: 'new-animal', cost: null, stats: {}, revision: 'r1' }));
    expect(doc.canUndo).toBe(false);
  });

  it('shows the actual database file and refreshes after external edits', async () => {
    const { rpc, platform, doc } = await setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Show database file' }));
    expect(platform.revealed[0]).toMatch(/animals\.db$/);
    rpc.on('mods.animalDatabase', () => ({ animals: [{ ...animal, name: 'SQL edited', stats: { adultMassKg: 6000 } }], fields }));
    rpc.on('mods.get', () => modDetail({ revision: 'sql-edited' }));
    await doc.refreshIfChanged();
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Display name' })).toHaveValue('SQL edited'));
    expect(screen.getByRole('spinbutton', { name: /Adult mass/ })).toHaveValue(6000);
  });

  it('creates a separate species from a template', async () => {
    const { rpc } = await setup([]);
    rpc.on('mods.addSpecies', () => modDetail({ revision: 'r2' }));
    await fireEvent.input(screen.getByRole('textbox', { name: 'Species id' }), { target: { value: 'new-animal' } });
    await fireEvent.input(screen.getByRole('textbox', { name: 'Display name' }), { target: { value: 'New animal' } });
    await fireEvent.input(screen.getByRole('textbox', { name: 'Genus' }), { target: { value: 'Example' } });
    await fireEvent.input(screen.getByRole('textbox', { name: 'Species epithet' }), { target: { value: 'animal' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Create species' }));
    await waitFor(() => expect(rpc.callsTo('mods.addSpecies')[0]?.params).toMatchObject({ speciesId: 'new-animal', template: 'Allosaurus Anax', stats: {}, revision: 'r1' }));
  });

  it('a new species starts with the chosen template skin', async () => {
    const { rpc } = await setup([]);
    rpc.on('mods.addSpecies', () => modDetail({ revision: 'r2' }));
    await waitFor(() => expect(within(screen.getByRole('combobox', { name: /Starting skin/ })).getAllByRole('option')).toHaveLength(2));
    await fireEvent.change(screen.getByRole('combobox', { name: /Starting skin/ }), { target: { value: '1' } });
    await fireEvent.input(screen.getByRole('textbox', { name: /Species id/ }), { target: { value: 'mirabilis' } });
    await fireEvent.input(screen.getByRole('textbox', { name: /Display name/ }), { target: { value: 'Mirabilis' } });
    await fireEvent.input(screen.getByRole('textbox', { name: /Genus/ }), { target: { value: 'Example' } });
    await fireEvent.input(screen.getByRole('textbox', { name: /Species epithet/ }), { target: { value: 'mirabilis' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Create species' }));
    await waitFor(() => expect(rpc.callsTo('mods.addSpecies')[0]?.params).toMatchObject({ template: 'Allosaurus Anax', startingSkin: 1 }));
  });

  it('changing the template puts the starting skin back to its first skin', async () => {
    await setup([]);
    const starting = () => screen.getByRole('combobox', { name: /Starting skin/ }) as HTMLSelectElement;
    await waitFor(() => expect(within(starting()).getAllByRole('option')).toHaveLength(2));
    await fireEvent.change(starting(), { target: { value: '1' } });
    expect(starting().value).toBe('1');

    await fireEvent.change(screen.getByRole('combobox', { name: /Template/ }), { target: { value: 'Carcharodontosaurus' } });

    await waitFor(() => expect(within(starting()).getAllByRole('option')).toHaveLength(3));
    expect(starting().value).toBe('0');
    expect(within(starting()).getByRole('option', { name: 'Base' })).toHaveProperty('selected', true);
  });

  async function fillNewSpecies() {
    await fireEvent.input(screen.getByRole('textbox', { name: /Species id/ }), { target: { value: 'mirabilis' } });
    await fireEvent.input(screen.getByRole('textbox', { name: /Display name/ }), { target: { value: 'Mirabilis' } });
    await fireEvent.input(screen.getByRole('textbox', { name: /Genus/ }), { target: { value: 'Example' } });
    await fireEvent.input(screen.getByRole('textbox', { name: /Species epithet/ }), { target: { value: 'mirabilis' } });
  }

  it("a new species' starting skin can copy another species' skin", async () => {
    const { rpc } = await setup([]);
    rpc.on('mods.addSpecies', () => modDetail({ revision: 'r2' }));
    await waitFor(() => expect(within(screen.getByRole('combobox', { name: 'Copy from' })).getAllByRole('option')).toHaveLength(2));
    await fireEvent.change(screen.getByRole('combobox', { name: 'Copy from' }), { target: { value: 'Carcharodontosaurus' } });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Starting skin' }), { target: { value: '2' } });
    await fillNewSpecies();
    await fireEvent.click(screen.getByRole('button', { name: 'Create species' }));
    await waitFor(() => expect(rpc.callsTo('mods.addSpecies')[0]?.params).toMatchObject({
      template: 'Allosaurus Anax', startingSkin: 0, startFrom: 'Carcharodontosaurus', startSkin: 2, startPng: null,
    }));
  });

  it('own PNGs need the male diffuse before the species can be created', async () => {
    const { platform } = await setup([]);
    await fillNewSpecies();
    await fireEvent.click(screen.getByRole('radio', { name: 'My own PNGs' }));
    expect(screen.getByRole('button', { name: 'Create species' })).toBeDisabled();
    platform.files.push('C:/art/male.png');
    await fireEvent.click(screen.getByRole('button', { name: 'Choose male diffuse PNG…' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create species' })).toBeEnabled());
  });

  it('a species with its own starting skin opens it instead of the template skin controls', async () => {
    const onOpenSkin = vi.fn();
    const base = modDetail();
    const detail = modDetail({ skins: [{ ...base.skins[0], id: 'new-animal-start', name: 'Mine', species: 'tyrant.red-spot.new-animal', starting: true }] });
    await setup([{ ...animal, startingSkinId: 'new-animal-start' } as typeof animal], { onOpenSkin }, true, detail);
    const skins = screen.getByRole('region', { name: 'Skins' });
    expect(within(skins).getByText('Starting skin: Mine')).toBeInTheDocument();
    expect(within(skins).queryByRole('combobox', { name: 'Starting skin' })).toBeNull();
    expect(within(skins).queryByRole('button', { name: 'Choose swatch PNG…' })).toBeNull();
    expect(within(skins).queryByRole('button', { name: 'Render from 3D…' })).toBeNull();
    await fireEvent.click(within(skins).getByRole('button', { name: 'Open starting skin' }));
    expect(onOpenSkin).toHaveBeenCalledWith('new-animal-start');
  });

  it('Make default makes another skin the starting skin after confirming', async () => {
    const base = modDetail();
    const detail = modDetail({ skins: [
      { ...base.skins[0], id: 'new-animal-start', name: 'Mine', species: 'tyrant.red-spot.new-animal', starting: true },
      { ...base.skins[0], id: 'brown', name: 'Brown', species: 'tyrant.red-spot.new-animal', starting: false },
    ] });
    const { rpc, platform } = await setup([{ ...animal, startingSkinId: 'new-animal-start' } as typeof animal], {}, true, detail);
    rpc.on('mods.makeStartingSkin', () => ({ ...detail, revision: 'r2' }));
    const skins = screen.getByRole('region', { name: 'Skins' });
    expect(within(skins).queryByRole('button', { name: 'Make Mine the default' })).toBeNull();
    platform.confirmAnswer = false;
    await fireEvent.click(within(skins).getByRole('button', { name: 'Make Brown the default' }));
    expect(platform.confirms[0]).toContain('Mine stays as an ordinary skin');
    expect(rpc.callsTo('mods.makeStartingSkin')).toHaveLength(0);
    platform.confirmAnswer = true;
    await fireEvent.click(within(skins).getByRole('button', { name: 'Make Brown the default' }));
    await waitFor(() => expect(rpc.callsTo('mods.makeStartingSkin')[0]?.params).toMatchObject({ skin: 'brown', revision: 'r1' }));
  });

  it('Delete species shows what goes, then removes it with its files', async () => {
    const { rpc } = await setup();
    rpc.on('mods.speciesRemoval', () => ({ key: animal.key, name: 'New animal', skins: ['Default'], models: 0, sounds: 0, prefabSettings: false, usedBy: [] }));
    rpc.on('mods.removeSpecies', () => modDetail({ revision: 'r2' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Delete species…' }));
    const dialog = await screen.findByRole('dialog', { name: 'Delete New animal' });
    expect(rpc.callsTo('mods.speciesRemoval')[0]?.params).toEqual({ id: 'red-spot', speciesId: 'new-animal' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Delete species' }));
    await waitFor(() => expect(rpc.callsTo('mods.removeSpecies')[0]?.params).toMatchObject({ speciesId: 'new-animal', deleteFiles: true }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('a species without one can be given an editable starting skin', async () => {
    const { rpc } = await setup([{ ...animal, startingSkin: 0, startingSkinName: null, swatch: null } as typeof animal]);
    rpc.on('mods.addStartingSkin', () => modDetail({ revision: 'r2' }));
    const skins = screen.getByRole('region', { name: 'Skins' });
    await fireEvent.click(within(skins).getByRole('button', { name: 'Make an editable starting skin' }));
    await fireEvent.change(within(skins).getByRole('combobox', { name: 'Copy from' }), { target: { value: 'Carcharodontosaurus' } });
    await fireEvent.click(within(skins).getByRole('button', { name: 'Create starting skin' }));
    await waitFor(() => expect(rpc.callsTo('mods.addStartingSkin')[0]?.params).toMatchObject({ speciesId: 'new-animal', startFrom: 'Carcharodontosaurus', startSkin: 0 }));
  });

  it("making a starting skin copies the template skin the species already starts with", async () => {
    const { rpc } = await setup([{ ...animal, startingSkin: 1, startingSkinName: null, swatch: null } as typeof animal]);
    rpc.on('mods.addStartingSkin', () => modDetail({ revision: 'r2' }));
    const skins = screen.getByRole('region', { name: 'Skins' });
    await fireEvent.click(within(skins).getByRole('button', { name: 'Make an editable starting skin' }));
    await fireEvent.click(within(skins).getByRole('button', { name: 'Create starting skin' }));
    await waitFor(() => expect(rpc.callsTo('mods.addStartingSkin')[0]?.params).toMatchObject({ startFrom: 'Allosaurus Anax', startSkin: 1 }));
  });

  it("the Skins section sets the starting skin, its name and swatch, lists the species' skins and adds one", async () => {
    const onAddSkin = vi.fn();
    const onOpenSkin = vi.fn();
    const base = modDetail();
    const detail = modDetail({ skins: [{ ...base.skins[0], id: 'blue', name: 'Blue', species: 'tyrant.red-spot.new-animal' }] });
    const { rpc, platform } = await setup([{ ...animal, key: 'tyrant.red-spot.new-animal', startingSkin: 0, startingSkinName: null, swatch: null } as typeof animal], { onAddSkin, onOpenSkin }, true, detail);
    const skins = screen.getByRole('region', { name: 'Skins' });
    await waitFor(() => expect(within(within(skins).getByRole('combobox', { name: 'Starting skin' })).getAllByRole('option')).toHaveLength(2));
    await fireEvent.change(within(skins).getByRole('combobox', { name: 'Starting skin' }), { target: { value: '1' } });
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesStartingSkin')[0]?.params).toMatchObject({ speciesId: 'new-animal', startingSkin: 1, name: null }));
    await fireEvent.change(within(skins).getByRole('textbox', { name: 'Starting skin name' }), { target: { value: 'Ghost' } });
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesStartingSkin')[1]?.params).toMatchObject({ name: 'Ghost' }));
    platform.files.push('C:/s.png');
    await fireEvent.click(within(skins).getByRole('button', { name: 'Choose swatch PNG…' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesSwatch')[0]?.params).toMatchObject({ speciesId: 'new-animal', png: 'C:/s.png' }));
    await fireEvent.click(within(skins).getByRole('button', { name: 'Blue' }));
    expect(onOpenSkin).toHaveBeenCalledWith('blue');
    await fireEvent.click(within(skins).getByRole('button', { name: 'Add skin' }));
    expect(onAddSkin).toHaveBeenCalledWith('tyrant.red-spot.new-animal');
  });
  it("the Sounds section lists the species' replacements, opens one and adds one for this species", async () => {
    const onAddSound = vi.fn();
    const onOpenSound = vi.fn();
    const call = { event: 'event:/X/Vox/Call', name: 'Social call', group: 'Calls', species: 'tyrant.red-spot.new-animal', skin: null, files: ['sounds/call.ogg'], volume: 1, agePitch: 1, chance: null };
    await setup([animal], { onAddSound, onOpenSound }, true, modDetail({ sounds: [call] }));
    const sounds = screen.getByRole('region', { name: 'Sounds' });

    await fireEvent.click(within(sounds).getByRole('button', { name: 'Edit Social call' }));
    expect(onOpenSound).toHaveBeenCalledWith({ event: call.event, species: call.species, skin: null });
    await fireEvent.click(within(sounds).getByRole('button', { name: 'Add a sound for this species' }));
    expect(onAddSound).toHaveBeenCalledWith('tyrant.red-spot.new-animal');
  });
});
