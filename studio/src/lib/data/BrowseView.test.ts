import { fireEvent, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import BrowseView from './BrowseView.svelte';

describe('BrowseView', () => {
  it('shows the object picked last even when an earlier answer arrives later', async () => {
    const rpc = new FakeRpc();
    rpc.on('data.types', () => ({ createdUtc: 'x', buildGuid: 'b', errors: [], types: [{ fullName: 'T.AnimalData', shortName: 'AnimalData', count: 2 }] }));
    rpc.on('data.objects', () => ({ names: ['Slow', 'Fast'] }));
    let releaseSlow!: () => void;
    rpc.on('data.object', (p) =>
      p.name === 'Slow'
        ? new Promise((resolve) => (releaseSlow = () => resolve({ type: 'T.AnimalData', name: 'Slow', json: { marker: 'slow' } })))
        : { type: 'T.AnimalData', name: 'Fast', json: { marker: 'fast' } },
    );
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    renderWith(BrowseView, session);

    await fireEvent.click(await screen.findByRole('button', { name: /AnimalData/ }));
    await fireEvent.click(await screen.findByRole('button', { name: 'Slow' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Fast' }));
    expect(await screen.findByText('"fast"')).toBeInTheDocument();
    releaseSlow();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(screen.queryByText('"slow"')).toBeNull();
    expect(screen.getByRole('heading', { name: 'Fast' })).toBeInTheDocument();
  });
});
