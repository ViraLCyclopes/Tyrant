import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import CleanSkins from './CleanSkins.svelte';

function setup(orphans = [{ species: 'Carcharodontosaurus', key: 'gone-mod/blue', number: 3 }]) {
  const rpc = new FakeRpc().on('mods.skinSlots', () => ({ orphans })).on('mods.forgetSkins', () => ({ orphans: [] }));
  const platform = new FakePlatform();
  return { rpc, platform, session: new Session(rpc, platform, memoryStore()) };
}

describe('CleanSkins', () => {
  it('forgets the chosen skins after confirming', async () => {
    const { rpc, platform, session } = setup();
    renderWith(CleanSkins, session);

    await fireEvent.click(await screen.findByRole('checkbox', { name: 'Forget gone-mod/blue' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Forget selected' }));

    await waitFor(() => expect(rpc.callsTo('mods.forgetSkins')[0]?.params).toEqual({ keys: ['gone-mod/blue'] }));
    expect(platform.confirms[0]).toContain('saved');
    expect(await screen.findByText(/No skin numbers to clean up/)).toBeInTheDocument();
  });

  it('forgets nothing when the confirmation is declined', async () => {
    const { rpc, platform, session } = setup();
    platform.confirmAnswer = false;
    renderWith(CleanSkins, session);

    await fireEvent.click(await screen.findByRole('checkbox', { name: 'Forget gone-mod/blue' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Forget selected' }));

    expect(rpc.callsTo('mods.forgetSkins')).toHaveLength(0);
  });

  it('says when there is nothing to clean up', async () => {
    const { session } = setup([]);
    renderWith(CleanSkins, session);

    expect(await screen.findByText(/No skin numbers to clean up/)).toBeInTheDocument();
  });
});
