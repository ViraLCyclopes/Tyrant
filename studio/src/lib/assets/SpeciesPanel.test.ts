import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus, messages } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import SpeciesPanel from './SpeciesPanel.svelte';
import { exportFormat } from './exportFormat.svelte';

function setup() {
  const rpc = new FakeRpc();
  rpc.on('species.list', () => ({
    species: [
      { key: 'carcharodontosaurus', displayName: 'Carcharodontosaurus', vivarium: false, group: 'g1', prefabRef: 'p#1', textures: 14 },
      { key: 'stegosaurus', displayName: 'Stegosaurus', vivarium: false, group: 'g2', prefabRef: 'p#2', textures: 9 },
    ],
  }));
  rpc.on('species.pack', (p) => ({
    directory: `D:\\ws\\assets\\species\\${p.key}`, models: 3, textures: 14, failed: 0, targetsPath: `D:\\ws\\assets\\species\\${p.key}\\targets.json`,
  }));
  rpc.on('workspace.status', () => workspaceStatus({ hasAssetIndex: true }));
  rpc.on('sounds.forSpecies', () => ({
    speciesId: 'Carcharodontosaurus', hasEventList: true,
    sounds: [{ event: 'event:/X/Roar', name: 'Social call', group: 'Calls', species: ['Carcharodontosaurus'], lengthMs: null, oneShot: null, perAnimal: true }],
  }));
  rpc.on('sounds.search', () => ({ hasEventList: true, sounds: [] }));
  rpc.on('species.ik', () => ({ chains: [{ name: 'Head', kind: 'head', joints: ['Neck', 'Head'], poleFrom: null, influence: 1 }] }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  session.workspace = workspaceStatus({ hasAssetIndex: true });
  return { rpc, platform, session };
}

describe('SpeciesPanel', () => {
  it('lists species and filters them by name', async () => {
    const { session } = setup();
    renderWith(SpeciesPanel, session);
    expect(await screen.findByText('Carcharodontosaurus')).toBeInTheDocument();

    await fireEvent.input(screen.getByRole('searchbox', { name: 'Find a species' }), { target: { value: 'carch' } });

    expect(screen.queryByText('Stegosaurus')).toBeNull();
    expect(screen.getByText('Carcharodontosaurus')).toBeInTheDocument();
  });

  it('exports a pack with FBX models too when chosen', async () => {
    const { rpc, session } = setup();
    renderWith(SpeciesPanel, session);

    await fireEvent.change(await screen.findByRole('combobox', { name: 'Model format' }), { target: { value: 'both' } });
    await fireEvent.click(await screen.findByRole('button', { name: 'Export Carcharodontosaurus pack' }));

    await waitFor(() => expect(rpc.callsTo('species.pack')[0]?.params).toEqual({ key: 'carcharodontosaurus', format: 'both' }));
    exportFormat.value = 'glb';
  });

  it('exports a species pack and offers to show it', async () => {
    const { rpc, platform, session } = setup();
    renderWith(SpeciesPanel, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Export Carcharodontosaurus pack' }));

    await waitFor(() => expect(rpc.callsTo('species.pack')[0]?.params).toEqual({ key: 'carcharodontosaurus' }));
    await waitFor(() => expect(messages(session, 'tab-test').join('\n')).toContain('Exported 3 models and 14 textures to D:\\ws\\assets\\species\\carcharodontosaurus.'));
    await fireEvent.click(await screen.findByRole('button', { name: 'Show in Explorer' }));
    expect(platform.revealed).toEqual(['D:\\ws\\assets\\species\\carcharodontosaurus']);
  });

  it('passes species pack notes on in the notice', async () => {
    const { rpc, session } = setup();
    rpc.on('species.pack', (p) => ({
      directory: `D:\ws\assets\species\${p.key}`, models: 3, textures: 14, failed: 0, targetsPath: 'x',
      notes: ['Texture T_Carcharo_D could not be decoded, so its material is plain: BC7'],
    }));
    renderWith(SpeciesPanel, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Export Carcharodontosaurus pack' }));

    await waitFor(() => expect(messages(session, 'tab-test').join('\n')).toContain('could not be decoded'));
  });

  it('a species pack with failures is a warning, so the tab gets a marker', async () => {
    const { rpc, session } = setup();
    rpc.on('species.pack', (p) => ({
      directory: `D:\\ws\\assets\\species\\${p.key}`, models: 3, textures: 12, failed: 2, targetsPath: 'x',
    }));
    renderWith(SpeciesPanel, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Export Carcharodontosaurus pack' }));

    await waitFor(() => expect(session.log.records.some((r) => r.level === 'warn' && r.message.includes('2'))).toBe(true));
  });

  it('opens Add a skin for a species', async () => {
    const { rpc, session } = setup();
    rpc.on('mods.species', () => ({ hasDump: false, species: [] })).on('mods.list', () => ({ mods: [], frameworkInstalled: false, frameworkOutdated: false }));
    renderWith(SpeciesPanel, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Add a skin to Carcharodontosaurus' }));

    expect(await screen.findByText(/Run data dump/)).toBeInTheDocument();
  });

  it("opens a species' sounds", async () => {
    const { rpc, session } = setup();
    renderWith(SpeciesPanel, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Sounds of Carcharodontosaurus' }));

    expect(await screen.findByText('Social call')).toBeInTheDocument();
    expect(rpc.callsTo('sounds.forSpecies')[0]?.params).toEqual({ species: 'carcharodontosaurus' });
  });

  it('opens All sounds', async () => {
    const { rpc, session } = setup();
    renderWith(SpeciesPanel, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'All sounds…' }));

    expect(await screen.findByRole('searchbox', { name: 'Find a sound' })).toBeInTheDocument();
    await waitFor(() => expect(rpc.callsTo('sounds.search')).toHaveLength(1));
  });

  it('shows a species IK chains', async () => {
    const { session } = setup();
    renderWith(SpeciesPanel, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'IK chains of Stegosaurus' }));

    expect(await screen.findByText('Neck → Head', { exact: false })).toBeInTheDocument();
  });
});
