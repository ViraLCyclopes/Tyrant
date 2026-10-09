import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { RpcError } from '$lib/rpc/client';
import { ModDoc } from './modDoc.svelte';
import PrefabPage from './PrefabPage.svelte';

const fields = {
  target: 'Spinosaurus',
  fields: [
    { object: '', component: 'AnimalPhysics', field: 'inputParams.waterlineHeight', kind: 'number', gameValue: '2.75', value: '3.5', editable: true, enumNames: null, enumValues: null },
    { object: '', component: 'AnimalPhysics', field: 'inputParams.mode', kind: 'enum', gameValue: '2', value: null, editable: true, enumNames: ['Walk', 'Swim'], enumValues: [0, 2] },
    { object: '', component: 'AnimalPhysics', field: 'swims', kind: 'bool', gameValue: 'true', value: null, editable: true, enumNames: null, enumValues: null },
    { object: 'IK/LegL', component: 'FABRIKComponentAnimal#1', field: 'core.influence', kind: 'number', gameValue: '1', value: null, editable: true, enumNames: null, enumValues: null },
    { object: '', component: 'AnimalPhysics', field: 'offset', kind: 'vector3', gameValue: '{"x":0,"y":1,"z":0}', value: null, editable: true, enumNames: null, enumValues: null },
    { object: 'Legs', component: 'Twin#1', field: 'speed', kind: 'duplicate', gameValue: '3', value: null, editable: false, enumNames: null, enumValues: null },
    { object: '', component: 'AnimalPhysics', field: 'target', kind: 'reference', gameValue: '{}', value: null, editable: false, enumNames: null, enumValues: null },
  ],
};

async function setup(detail = modDetail()) {
  const rpc = new FakeRpc().on('mods.get', () => detail).on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
    .on('mods.prefabFields', () => fields).on('mods.setPrefabField', () => modDetail({ revision: 'r2' })).on('mods.resetPrefabField', () => modDetail({ revision: 'r3' }));
  const session = new Session(rpc, new FakePlatform(), memoryStore());
  const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
  tab.key = 'red-spot';
  const doc = new ModDoc('red-spot', rpc, tab, () => {});
  await doc.load();
  const view = renderWith(PrefabPage, session, { doc, target: 'Spinosaurus' }, tab);
  await screen.findByText('inputParams.waterlineHeight');
  return { rpc, view };
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

  it('a rejected vector part is not sent again with the next part', async () => {
    const { rpc } = await setup();
    let first = true;
    rpc.on('mods.setPrefabField', () => {
      if (first) {
        first = false;
        throw new RpcError('x is not allowed', 'MOD_INVALID');
      }
      return modDetail({ revision: 'r2' });
    });
    await fireEvent.change(screen.getByRole('spinbutton', { name: 'offset x' }), { target: { value: '99' } });
    await waitFor(() => expect(rpc.callsTo('mods.setPrefabField')).toHaveLength(1));
    await fireEvent.change(screen.getByRole('spinbutton', { name: 'offset y' }), { target: { value: '5' } });
    await waitFor(() => expect(rpc.callsTo('mods.setPrefabField')).toHaveLength(2));
    expect((rpc.callsTo('mods.setPrefabField')[1]?.params as { value: unknown }).value).toEqual({ x: 0, y: 5, z: 0 });
  });

  it('a duplicate-path component is shown with a note, not as an editor', async () => {
    await setup();
    expect(screen.getByText("3 (duplicate path — the game can't address it)")).toBeInTheDocument();
    expect(screen.queryByRole('spinbutton', { name: 'speed' })).toBeNull();
  });

  it('lists settings the game no longer has, removes them, and Remove all works with a file but nothing changed', async () => {
    const file = JSON.stringify({ format: 1, target: 'Spinosaurus', fields: [{ object: '', component: 'AnimalPhysics', field: 'oldField', value: 7 }] });
    const detail = modDetail({ prefabFiles: { 'spinosaurus.json': file }, prefabTargets: [{ target: 'Spinosaurus', file: 'spinosaurus.json', fields: 1 }] });
    const { rpc } = await setup(detail);
    const group = await screen.findByRole('group', { name: 'No longer in the game' });
    expect(within(group).getByText('(root) AnimalPhysics oldField')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Remove all…' })).toBeEnabled();
    await fireEvent.click(within(group).getByRole('button', { name: 'Reset oldField' }));
    await waitFor(() => expect(rpc.callsTo('mods.resetPrefabField')[0]?.params).toMatchObject({ target: 'Spinosaurus', object: '', component: 'AnimalPhysics', field: 'oldField' }));
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

  it('shows the loading line, not the old fields, while another target loads', async () => {
    const { rpc, view } = await setup();
    rpc.on('mods.prefabFields', () => new Promise(() => {}));
    await view.rerender({ target: 'Allosaurus' });
    expect(screen.getByText('Reading the prefab…')).toBeInTheDocument();
    expect(screen.queryByText('inputParams.waterlineHeight')).toBeNull();
  });

  it('a failed read says so', async () => {
    const { rpc, view } = await setup();
    rpc.on('mods.prefabFields', () => {
      throw new Error('boom');
    });
    await view.rerender({ target: 'Allosaurus' });
    expect(await screen.findByText('The prefab could not be read; the log says why.')).toBeInTheDocument();
  });
});
