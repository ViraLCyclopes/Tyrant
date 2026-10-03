import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import SpeciesPanel from './SpeciesPanel.svelte';

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

  it('exports a species pack and offers to show it', async () => {
    const { rpc, platform, session } = setup();
    renderWith(SpeciesPanel, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Export Carcharodontosaurus pack' }));

    await waitFor(() => expect(rpc.callsTo('species.pack')[0]?.params).toEqual({ key: 'carcharodontosaurus' }));
    await waitFor(() => expect(session.notice).toBe('Exported 3 models and 14 textures to D:\\ws\\assets\\species\\carcharodontosaurus.'));
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

    await waitFor(() => expect(session.notice).toContain('could not be decoded'));
  });

  it('opens Add a skin for a species', async () => {
    const { rpc, session } = setup();
    rpc.on('mods.species', () => ({ hasDump: false, species: [] })).on('mods.list', () => ({ mods: [], frameworkInstalled: false }));
    renderWith(SpeciesPanel, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Add a skin to Carcharodontosaurus' }));

    expect(await screen.findByText(/Run data dump/)).toBeInTheDocument();
  });
});
