import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { DataCompareParams } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import CompareView from './CompareView.svelte';

describe('CompareView', () => {
  it('starts with only the differences and can show every field', async () => {
    const rpc = new FakeRpc();
    rpc.on('data.compare', (p) =>
      p.onlyDifferences
        ? { names: ['Stego', 'Ankylo'], fields: [], values: [] }
        : { names: ['Stego', 'Ankylo'], fields: ['diet'], values: [['Herbivore', 'Herbivore']] },
    );
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    renderWith(CompareView, session, { type: 'T.AnimalData', names: ['Stego', 'Ankylo'], onClose: vi.fn() });

    expect(await screen.findByText('No differences.')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Only differences' }));

    await waitFor(() => expect((rpc.callsTo('data.compare').at(-1)?.params as DataCompareParams).onlyDifferences).toBe(false));
    expect(await screen.findAllByText('Herbivore')).toHaveLength(2);
  });
});
