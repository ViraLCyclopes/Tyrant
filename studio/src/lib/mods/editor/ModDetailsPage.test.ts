import { screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { Tab } from '$lib/shell/tab.svelte';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import { ModDoc } from './modDoc.svelte';
import ModDetailsPage from './ModDetailsPage.svelte';

describe('Mod details page', () => {
  it('says which other mods species it uses and whether they are installed', async () => {
    const detail = modDetail({ usesSpeciesFrom: [{ modId: 'mod-a', modName: 'Mod A', skins: 3, inWorkspace: true, installed: false, enabled: false }] });
    const rpc = new FakeRpc()
      .on('mods.get', () => detail)
      .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }));
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
    const doc = new ModDoc('red-spot', rpc, tab, () => {});
    await doc.load();
    renderWith(ModDetailsPage, session, { doc }, tab);
    expect(await screen.findByText(/Uses species from: Mod A/)).toBeInTheDocument();
    expect(screen.getByText(/not installed — 3 skins skipped/)).toBeInTheDocument();
  });
});
