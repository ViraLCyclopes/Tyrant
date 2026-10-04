import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { SoundDto } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import SpeciesSounds from './SpeciesSounds.svelte';

const sound = (name: string, group: string, species: string[]): SoundDto => ({ event: `event:/X/${name}`, name, group, species, lengthMs: null, oneShot: null });

function setup(speciesId: string | null = 'Carcharodontosaurus') {
  const rpc = new FakeRpc()
    .on('sounds.forSpecies', () => ({
      speciesId,
      hasEventList: true,
      sounds: speciesId ? [sound('Social call', 'Calls', ['Acrocanthosaurus', 'Carcharodontosaurus']), sound('Footstep', 'Footsteps', ['Carcharodontosaurus'])] : [],
    }))
    .on('mods.list', () => ({ mods: [] }));
  return { rpc, session: new Session(rpc, new FakePlatform(), memoryStore()) };
}

describe('SpeciesSounds', () => {
  it("lists the species' sounds by group and says which are shared", async () => {
    const { rpc, session } = setup();
    renderWith(SpeciesSounds, session, { speciesKey: 'carcharodontosaurus', displayName: 'Carcharodontosaurus' });

    expect(await screen.findByText('Social call')).toBeInTheDocument();
    expect(screen.getByText('Calls (1)')).toBeInTheDocument();
    expect(screen.getByText('Footsteps (1)')).toBeInTheDocument();
    expect(screen.getByText('shared with 2 species')).toBeInTheDocument();
    expect(rpc.callsTo('sounds.forSpecies')[0]?.params).toEqual({ species: 'carcharodontosaurus' });
  });

  it("Replace… offers this species' own replacement", async () => {
    const { session } = setup();
    renderWith(SpeciesSounds, session, { speciesKey: 'carcharodontosaurus', displayName: 'Carcharodontosaurus' });

    await fireEvent.click(await screen.findByRole('button', { name: 'Replace Social call' }));

    await waitFor(() => expect(screen.getByRole('radio', { name: 'Only Carcharodontosaurus' })).toBeChecked());
  });

  it('says when the data dump has no sounds for the species', async () => {
    const { session } = setup(null);
    renderWith(SpeciesSounds, session, { speciesKey: 'frog', displayName: 'Frog' });

    expect(await screen.findByText(/no sounds for Frog/)).toBeInTheDocument();
  });
});
