import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import PrefabPage from './PrefabPage.svelte';

const fields = {
  target: 'Spinosaurus',
  fields: [
    { object: '', component: 'AnimalPhysics', field: 'inputParams.waterlineHeight', kind: 'number', gameValue: '2.75', value: '3.5', editable: true, enumNames: null, enumValues: null },
    { object: '', component: 'AnimalPhysics', field: 'inputParams.mode', kind: 'enum', gameValue: '2', value: null, editable: true, enumNames: ['Walk', 'Swim'], enumValues: [0, 2] },
    { object: '', component: 'AnimalPhysics', field: 'swims', kind: 'bool', gameValue: 'true', value: null, editable: true, enumNames: null, enumValues: null },
    { object: 'IK/LegL', component: 'FABRIKComponentAnimal#1', field: 'core.influence', kind: 'number', gameValue: '1', value: null, editable: true, enumNames: null, enumValues: null },
    { object: '', component: 'AnimalPhysics', field: 'target', kind: 'reference', gameValue: '{}', value: null, editable: false, enumNames: null, enumValues: null },
  ],
};

async function setup() {
  const rpc = new FakeRpc().on('mods.get', () => modDetail()).on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
    .on('mods.prefabFields', () => fields).on('mods.setPrefabField', () => modDetail({ revision: 'r2' })).on('mods.resetPrefabField', () => modDetail({ revision: 'r3' }));
  const session = new Session(rpc, new FakePlatform(), memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  tab.key = 'red-spot';
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  renderWith(PrefabPage, session, { doc, target: 'Spinosaurus' }, tab);
  await screen.findByText('inputParams.waterlineHeight');
  return { rpc };
}

describe('PrefabPage', () => {
  it('lists fields by object and component with the game value and the override', async () => {
    await setup();
    expect(screen.getByRole('heading', { name: /Spinosaurus/ })).toHaveTextContent('1 changed');
    expect(screen.getByRole('group', { name: '(root) AnimalPhysics' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'IK/LegL FABRIKComponentAnimal#1' })).toBeInTheDocument();
    expect(screen.getByRole('spinbutton', { name: 'inputParams.waterlineHeight' })).toHaveValue(3.5);
    expect(screen.getByText('game: 2.75')).toBeInTheDocument();
    expect(screen.getByText('target')).toBeInTheDocument(); // read-only shown
  });

  it('a change saves the field; enums send names; reset goes back to the game value', async () => {
    const { rpc } = await setup();
    const input = screen.getByRole('spinbutton', { name: 'inputParams.waterlineHeight' });
    await fireEvent.input(input, { target: { value: '4' } });
    await fireEvent.change(input);
    await waitFor(() => expect(rpc.callsTo('mods.setPrefabField')[0]?.params).toMatchObject({ target: 'Spinosaurus', object: '', component: 'AnimalPhysics', field: 'inputParams.waterlineHeight', value: 4 }));

    await fireEvent.change(screen.getByRole('combobox', { name: 'inputParams.mode' }), { target: { value: 'Swim' } });
    await waitFor(() => expect(rpc.callsTo('mods.setPrefabField')[1]?.params).toMatchObject({ field: 'inputParams.mode', value: 'Swim' }));

    await fireEvent.click(screen.getByRole('button', { name: 'Reset inputParams.waterlineHeight' }));
    await waitFor(() => expect(rpc.callsTo('mods.resetPrefabField')[0]?.params).toMatchObject({ field: 'inputParams.waterlineHeight' }));
  });

  it('shows the enum name of the real value, not its index', async () => {
    await setup();
    expect(screen.getByRole('combobox', { name: 'inputParams.mode' })).toHaveValue('Swim');
  });

  it('search and show-only-changed filter the list', async () => {
    await setup();
    await fireEvent.input(screen.getByRole('searchbox', { name: 'Find a field' }), { target: { value: 'influence' } });
    expect(screen.queryByText('inputParams.waterlineHeight')).toBeNull();
    expect(screen.getByText('core.influence')).toBeInTheDocument();
    await fireEvent.input(screen.getByRole('searchbox', { name: 'Find a field' }), { target: { value: '' } });
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Show only changed' }));
    expect(screen.getByText('inputParams.waterlineHeight')).toBeInTheDocument();
    expect(screen.queryByText('swims')).toBeNull();
  });
});
