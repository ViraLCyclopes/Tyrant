import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import type { DataQueryParams, DataQueryResult } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import TableView from './TableView.svelte';

const ANIMAL = 'PrehistoricKingdom.AnimalData';

function queryResult(overrides: Partial<DataQueryResult> = {}): DataQueryResult {
  return {
    type: ANIMAL,
    allColumns: ['$name', 'cost', 'diet'],
    columns: ['$name', 'cost', 'diet'],
    rows: [
      { name: 'Stego', values: ['Stego', '100', 'Herbivore'] },
      { name: 'Rex', values: ['Rex', '10', 'Carnivore'] },
    ],
    total: 2,
    page: 0,
    pageSize: 100,
    ...overrides,
  };
}

function setup() {
  const rpc = new FakeRpc();
  const platform = new FakePlatform();
  const store = memoryStore();
  const session = new Session(rpc, platform, store);
  session.workspace = workspaceStatus({ hasData: true });
  rpc.on('data.types', () => ({
    createdUtc: '2026-10-02T10:00:00Z',
    buildGuid: 'b1',
    errors: [],
    types: [
      { fullName: 'PrehistoricKingdom.AudioDatabase', shortName: 'AudioDatabase', count: 1 },
      { fullName: ANIMAL, shortName: 'AnimalData', count: 2 },
    ],
  }));
  rpc.on('data.query', () => queryResult());
  return { rpc, platform, store, session };
}

const lastQuery = (rpc: FakeRpc) => rpc.callsTo('data.query').at(-1)?.params as DataQueryParams;

describe('TableView', () => {
  it('opens AnimalData first and shows its rows', async () => {
    const { rpc, session } = setup();
    renderWith(TableView, session);

    expect(await screen.findByText('Herbivore')).toBeInTheDocument();
    expect(lastQuery(rpc)).toMatchObject({ type: ANIMAL, page: 0, columns: null });
  });

  it('sorting a column asks the core for that order, then reverses it', async () => {
    const { rpc, session } = setup();
    renderWith(TableView, session);
    await screen.findByText('Herbivore');

    await fireEvent.click(screen.getByRole('button', { name: /^cost/ }));
    await waitFor(() => expect(lastQuery(rpc)).toMatchObject({ sort: 'cost', descending: false }));
    await fireEvent.click(screen.getByRole('button', { name: /^cost/ }));
    await waitFor(() => expect(lastQuery(rpc)).toMatchObject({ sort: 'cost', descending: true }));
  });

  it('pages through the results', async () => {
    const { rpc, session } = setup();
    rpc.on('data.query', (p) => queryResult({ total: 250, page: p.page ?? 0 }));
    renderWith(TableView, session);
    expect(await screen.findByText(/Page 1 of 3/)).toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Next' }));

    await waitFor(() => expect(lastQuery(rpc)).toMatchObject({ page: 1 }));
    expect(await screen.findByText(/Page 2 of 3/)).toBeInTheDocument();
  });

  it('filters once typing stops', async () => {
    const { rpc, session } = setup();
    renderWith(TableView, session);
    await screen.findByText('Herbivore');

    await fireEvent.input(screen.getByRole('searchbox', { name: 'Filter rows' }), { target: { value: 'herb' } });

    await waitFor(() => expect(lastQuery(rpc)).toMatchObject({ filter: 'herb', page: 0 }));
  });

  it('compares the selected rows', async () => {
    const { rpc, session } = setup();
    rpc.on('data.compare', () => ({ names: ['Stego', 'Rex'], fields: ['cost'], values: [['100', '10']] }));
    renderWith(TableView, session);
    await screen.findByText('Herbivore');

    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Stego' }));
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Rex' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Compare (2)' }));

    expect(await screen.findByText('Comparing 2')).toBeInTheDocument();
    expect(rpc.callsTo('data.compare')[0].params).toEqual({ type: ANIMAL, names: ['Stego', 'Rex'], onlyDifferences: true });
  });

  it('exports to the file the user picks', async () => {
    const { rpc, platform, session } = setup();
    platform.saves.push('D:\\out\\AnimalData.csv');
    rpc.on('data.export', (p) => ({ path: p.path!, count: 2 }));
    renderWith(TableView, session);
    await screen.findByText('Herbivore');

    await fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }));

    await waitFor(() => expect(rpc.callsTo('data.export')[0]?.params).toEqual({ type: ANIMAL, format: 'csv', path: 'D:\\out\\AnimalData.csv' }));
    await waitFor(() => expect(session.notice).toBe('Exported 2 objects to D:\\out\\AnimalData.csv.'));
  });

  it('remembers the chosen columns per type', async () => {
    const { rpc, store, session } = setup();
    renderWith(TableView, session);
    await screen.findByText('Herbivore');

    await fireEvent.click(screen.getByRole('button', { name: 'Columns (2/2)' }));
    await fireEvent.click(screen.getByRole('checkbox', { name: 'diet' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(lastQuery(rpc)).toMatchObject({ columns: ['cost'] }));
    expect(store.get(`tyrant.columns.${ANIMAL}`)).toBe('["cost"]');
  });

  it('typing a filter keeps an unrelated error on screen', async () => {
    const { rpc, session } = setup();
    renderWith(TableView, session);
    await screen.findByText('Herbivore');
    session.error = new RpcError('No dump arrived.', 'DUMP_TIMEOUT');

    await fireEvent.input(screen.getByRole('searchbox', { name: 'Filter rows' }), { target: { value: 'herb' } });

    await waitFor(() => expect(lastQuery(rpc)).toMatchObject({ filter: 'herb' }));
    expect(session.error?.code).toBe('DUMP_TIMEOUT');
  });
});
