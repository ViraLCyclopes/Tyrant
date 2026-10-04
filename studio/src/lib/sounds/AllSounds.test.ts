import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import AllSounds from './AllSounds.svelte';

const click = { event: 'event:/User Interface/Buttons/UI_Click', name: 'Click', group: 'Interface', species: [], lengthMs: 90, oneShot: true };

function setup(hasEventList = true) {
  const rpc = new FakeRpc()
    .on('sounds.search', (p) => ({ hasEventList, sounds: (p as { text: string }).text === 'click' ? [click] : [] }))
    .on('mods.list', () => ({ mods: [] }));
  return { rpc, session: new Session(rpc, new FakePlatform(), memoryStore()) };
}

describe('AllSounds', () => {
  it('searches every game sound as you type and replaces one for everyone', async () => {
    const { rpc, session } = setup();
    renderWith(AllSounds, session);

    await fireEvent.input(screen.getByRole('searchbox', { name: 'Find a sound' }), { target: { value: 'click' } });

    expect(await screen.findByText('Click')).toBeInTheDocument();
    expect(screen.getByText('Interface')).toBeInTheDocument();
    expect(rpc.callsTo('sounds.search').at(-1)?.params).toEqual({ text: 'click' });
    await fireEvent.click(screen.getByRole('button', { name: 'Replace Click' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Choose files…' })).toBeInTheDocument());
    expect(screen.queryByRole('radio')).toBeNull();
  });

  it('asks for a new data dump when the dump has no event list', async () => {
    const { session } = setup(false);
    renderWith(AllSounds, session);

    expect(await screen.findByText(/Run data dump on the Workspace tab to list every sound/)).toBeInTheDocument();
  });
});
