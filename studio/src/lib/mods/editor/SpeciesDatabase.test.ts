import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import SpeciesDatabase from './SpeciesDatabase.svelte';

const animal = { id: 'new-animal', key: 'tyrant.red-spot.new-animal', base: 'Allosaurus Anax', name: 'New animal', genus: 'Example', epithet: 'animal', cost: null, stats: { adultMassKg: 4500 }, inheritedCost: 20000, inheritedStats: { adultMassKg: 4010, maxAgeYears: 40 }, dataFields: [{ path: '/brain/needs/adult/native/hitPoints', kind: 'number', value: '660', templateValue: '660', editable: true }], localization: { description: 'pack.description' }, icon: null };
const fields = [{ id: 'adultMassKg', label: 'Adult mass', unit: 'kg', min: 0.001, max: 1000000 }, { id: 'maxAgeYears', label: 'Lifespan', unit: 'years', min: 0.001, max: 10000 }];

async function setup(animals = [animal]) {
  const rpc = new FakeRpc()
    .on('mods.get', () => modDetail())
    .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
    .on('mods.species', () => ({ hasDump: true, species: [{ speciesId: 'Allosaurus Anax', vivarium: false, skins: [{ index: 0, name: 'Default', male: true, female: true }], prefabGuid: null }] }))
    .on('mods.animalDatabase', () => ({ animals, fields }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  renderWith(SpeciesDatabase, session, { doc }, tab);
  await screen.findByRole('spinbutton', { name: /Adult mass/ });
  return { rpc, platform, doc };
}

describe('Species database', () => {
  it('attaches a Unity bundle using the exporter asset path', async () => {
    const { rpc, platform } = await setup();
    rpc.on('mods.setSpeciesPrefab', () => modDetail({ revision: 'r2' }));
    await fireEvent.input(screen.getByRole('textbox', { name: 'Prefab asset path' }), { target: { value: 'assets/animals/mirabilis.prefab' } });
    platform.files.push('C:/exports/mirabilis.bundle');
    await fireEvent.click(screen.getByRole('button', { name: 'Attach Unity prefab bundle…' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesPrefab')[0]?.params).toMatchObject({ speciesId: 'new-animal', bundle: 'C:/exports/mirabilis.bundle', asset: 'assets/animals/mirabilis.prefab' }));
  });
  it('saves full fields and localization keys and imports a custom icon', async () => {
    const { rpc, platform } = await setup();
    rpc.on('mods.setSpecies', () => modDetail({ revision: 'r2' }));
    await fireEvent.input(screen.getByRole('textbox', { name: '/brain/needs/adult/native/hitPoints' }), { target: { value: '800' } });
    await fireEvent.input(screen.getByRole('textbox', { name: 'description key' }), { target: { value: 'pack.mirabilis.description' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save species' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpecies')[0]?.params).toMatchObject({ dataFields: { '/brain/needs/adult/native/hitPoints': '800' }, localization: { description: 'pack.mirabilis.description' } }));
    rpc.on('mods.setSpeciesIcon', () => modDetail({ revision: 'r3' }));
    platform.files.push('C:/icons/mirabilis.png');
    await fireEvent.click(screen.getByRole('button', { name: 'Choose icon PNG…' }));
    await waitFor(() => expect(rpc.callsTo('mods.setSpeciesIcon')[0]?.params).toMatchObject({ speciesId: 'new-animal', png: 'C:/icons/mirabilis.png' }));
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
});
