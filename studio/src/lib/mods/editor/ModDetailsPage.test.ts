import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import type { ModTexturesResult } from '$lib/rpc/types.gen';
import { describe, expect, it } from 'vitest';
import { RpcError } from '$lib/rpc/client';
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

  it('says whether the mod folder’s cooked pack is up to date, and Cook textures writes it there', async () => {
    let pack = 'missing';
    const facts = (): ModTexturesResult => ({
      compression: 'auto',
      textures: [{ file: 'skins/blue/male_D.png', kind: 'colour', mode: 'auto', override: false, format: 'DXT1', width: 4, height: 4, bytes: 16 }],
      totalBytes: 16, pack, packBytes: pack === 'missing' ? 0 : 5 * 1024 * 1024,
    });
    const rpc = new FakeRpc()
      .on('mods.get', () => modDetail())
      .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
      .on('mods.textures', facts)
      .on('mods.cookTextures', () => {
        pack = 'current';
        return { message: "Cooked 'red-spot's textures into C:\ws\mods\red-spot\textures.tpack.", path: 'C:\ws\mods\red-spot\textures.tpack' };
      });
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
    const doc = new ModDoc('red-spot', rpc, tab, () => {});
    await doc.load();
    renderWith(ModDetailsPage, session, { doc }, tab);

    expect(await screen.findByText(/Not cooked yet/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Cook textures' }));

    await waitFor(() => expect(rpc.callsTo('mods.cookTextures')[0]?.params).toEqual({ id: 'red-spot' }));
    expect(await screen.findByText(/textures\.tpack in the mod's folder is up to date \(5 MB\)/)).toBeInTheDocument();
  });

  it('says when the pack is out of date', async () => {
    const rpc = new FakeRpc()
      .on('mods.get', () => modDetail())
      .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
      .on('mods.textures', () => ({ compression: 'auto', textures: [{ file: 'a.png', kind: 'colour', mode: 'auto', override: false, format: 'DXT1', width: 4, height: 4, bytes: 16 }], totalBytes: 16, pack: 'stale', packBytes: 2048 }));
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
    const doc = new ModDoc('red-spot', rpc, tab, () => {});
    await doc.load();
    renderWith(ModDetailsPage, session, { doc }, tab);
    expect(await screen.findByText(/out of date/)).toBeInTheDocument();
  });

  describe('size ranges', () => {
    const ranges = { nursery: null, breeding: [0.9, 1.1], gameNursery: [0.8, 1.2], gameBreeding: [0.85, 1.15] };
    async function sizeSetup(refuse = false) {
      const rpc = new FakeRpc()
        .on('mods.get', () => modDetail())
        .on('mods.check', () => ({ errors: [], warnings: [], missingCutouts: [] }))
        .on('mods.sizeRanges', () => ranges)
        .on('mods.setSizeRanges', () => {
          if (refuse) throw new RpcError('The mod folder is read-only.', 'IO_ERROR');
          return modDetail({ revision: 'r2' });
        });
      const session = new Session(rpc, new FakePlatform(), memoryStore());
      const tab = new Tab('tab-mod', session.log, { retitle: () => {}, openTool: () => null });
      const doc = new ModDoc('red-spot', rpc, tab, () => {});
      await doc.load();
      renderWith(ModDetailsPage, session, { doc }, tab);
      await screen.findByText('Nursery size slider range');
      return rpc;
    }

    it('shows the game ranges and the note, and sets the nursery range with the breeding range kept', async () => {
      const rpc = await sizeSetup();
      expect(screen.getByText(/Random eggs keep the game's range/)).toBeInTheDocument();
      expect(screen.getByText('Breeding size range')).toBeInTheDocument();
      expect(screen.getByText(/Game: 0.80 – 1.20/)).toBeInTheDocument();
      await fireEvent.click(within(screen.getByRole('group', { name: 'Nursery size slider range' })).getByRole('checkbox', { name: /Game's range/ }));
      await waitFor(() => expect(rpc.callsTo('mods.setSizeRanges')).toHaveLength(1));
      expect(rpc.callsTo('mods.setSizeRanges')[0].params).toMatchObject({ nursery: [0.8, 1.2], breeding: [0.9, 1.1] });
    });

    it('a save that fails puts the saved range back', async () => {
      const rpc = await sizeSetup(true);
      const game = () => within(screen.getByRole('group', { name: 'Nursery size slider range' })).getByRole('checkbox', { name: /Game's range/ });
      expect(game()).toBeChecked();
      await fireEvent.click(game());
      await waitFor(() => expect(rpc.callsTo('mods.setSizeRanges')).toHaveLength(1));
      await waitFor(() => expect(game()).toBeChecked());
    });

    it('never sends a minimum above the maximum', async () => {
      const rpc = await sizeSetup();
      const from = screen.getByRole('slider', { name: 'Breeding size range from' });
      await fireEvent.input(from, { target: { value: '3' } });
      await fireEvent.change(from);
      await waitFor(() => expect(rpc.callsTo('mods.setSizeRanges')).toHaveLength(1));
      const [min, max] = (rpc.callsTo('mods.setSizeRanges')[0].params as { breeding: number[] }).breeding;
      expect(min).toBeLessThanOrEqual(max);
      expect(max).toBe(1.1);
    });
  });
});
