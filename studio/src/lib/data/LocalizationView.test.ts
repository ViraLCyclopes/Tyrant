import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { LocalizationQueryParams } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import LocalizationView from './LocalizationView.svelte';

function setup() {
  const rpc = new FakeRpc();
  rpc.on('data.languages', () => ({
    languages: [
      { code: 'en', name: 'English', termCount: 2 },
      { code: 'fr', name: 'Français', termCount: 1 },
    ],
  }));
  rpc.on('data.localization', (p) => {
    const languages = p.languages ?? [];
    return {
      languages,
      rows: [{ term: 'Animals/Stego', values: languages.map((l) => (l === 'fr' ? 'Stégosaure' : 'Stegosaurus')) }],
      total: 1,
      page: 0,
      pageSize: 100,
    };
  });
  return { rpc, session: new Session(rpc, new FakePlatform(), memoryStore()) };
}

const lastQuery = (rpc: FakeRpc) => rpc.callsTo('data.localization').at(-1)?.params as LocalizationQueryParams;

describe('LocalizationView', () => {
  it('starts with English and adds a language when it is ticked', async () => {
    const { rpc, session } = setup();
    renderWith(LocalizationView, session);

    expect(await screen.findByText('Stegosaurus')).toBeInTheDocument();
    expect(lastQuery(rpc).languages).toEqual(['en']);
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Français' }));

    await waitFor(() => expect(lastQuery(rpc).languages).toEqual(['en', 'fr']));
    expect(await screen.findByText('Stégosaure')).toBeInTheDocument();
  });

  it('searches terms and text', async () => {
    const { rpc, session } = setup();
    renderWith(LocalizationView, session);
    await screen.findByText('Stegosaurus');

    await fireEvent.input(screen.getByRole('searchbox', { name: 'Search localization' }), { target: { value: 'stego' } });

    await waitFor(() => expect(lastQuery(rpc)).toMatchObject({ filter: 'stego', page: 0 }));
  });
});
