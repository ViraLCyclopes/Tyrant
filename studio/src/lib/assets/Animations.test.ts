import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import type { AnimationInfo, BlenderStatusDto } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import Animations from './Animations.svelte';

const ready: BlenderStatusDto = {
  found: true, exe: 'b.exe', version: '5.2.2', supported: true, missingConfigured: null,
  addon: 'current', addonInstalled: '0.1.0', addonBundled: '0.1.0', problem: null,
};

function info(name: string, over: Partial<AnimationInfo> = {}): AnimationInfo {
  return { id: `Carch|${name}`, name, length: 2.5, frameRate: 24, loops: false, travels: false, ...over };
}

function setup(animations: AnimationInfo[]) {
  const rpc = new FakeRpc()
    .on('species.animations', () => ({ species: 'Carcharodontosaurus', animations }))
    .on('species.exportAnimations', () => ({ directory: 'D:\\ws\\assets\\animations\\Carcharodontosaurus', files: ['a.fbx'], notes: [] }))
    .on('blender.status', () => ready)
    .on('blender.open', () => ({ projectFile: 'p', how: 'running', gameChanged: false }));
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  session.workspace = workspaceStatus({ hasAssetIndex: true });
  return { rpc, platform, session };
}

describe('Animations', () => {
  it('lists the animations with their length and whether they loop or travel, and filters by name', async () => {
    const { rpc, session } = setup([info('LocWalk', { loops: true, travels: true }), info('Roar')]);
    renderWith(Animations, session, { speciesKey: 'carcharodontosaurus', displayName: 'Carcharodontosaurus' });

    expect(await screen.findByText('LocWalk')).toBeInTheDocument();
    expect(screen.getByText(/2\.50 s · loops · travels/)).toBeInTheDocument();
    expect(rpc.callsTo('species.animations')[0]?.params).toEqual({ species: 'carcharodontosaurus' });

    await fireEvent.input(screen.getByLabelText('Search animations'), { target: { value: 'roa' } });
    expect(screen.queryByText('LocWalk')).toBeNull();
    expect(screen.getByText('Roar')).toBeInTheDocument();
  });

  it('exports the ticked animations as FBX, one file each, by default', async () => {
    const { rpc, session } = setup([info('LocWalk'), info('Roar'), info('Idle')]);
    renderWith(Animations, session, { speciesKey: 'carcharodontosaurus', displayName: 'Carcharodontosaurus' });

    await fireEvent.click(await screen.findByLabelText('Pick LocWalk'));
    await fireEvent.click(screen.getByLabelText('Pick Roar'));
    await fireEvent.click(screen.getByRole('button', { name: 'Export animations…' }));

    await waitFor(() => expect(rpc.callsTo('species.exportAnimations')[0]?.params).toEqual({
      species: 'Carcharodontosaurus', ids: ['Carch|LocWalk', 'Carch|Roar'], format: 'fbx', singleFile: false,
    }));
  });

  it('opens the model in Blender with the ticked animations', async () => {
    const { rpc, session } = setup([info('LocWalk'), info('Roar')]);
    renderWith(Animations, session, { speciesKey: 'carcharodontosaurus', displayName: 'Carcharodontosaurus' });

    await fireEvent.click(await screen.findByLabelText('Pick Roar'));
    await fireEvent.click(screen.getByRole('button', { name: 'Open in Blender with these' }));

    await waitFor(() => expect(rpc.callsTo('blender.open')[0]?.params).toMatchObject({ species: 'Carcharodontosaurus', animations: ['Carch|Roar'] }));
  });

  it('asks before ticking more than fifty animations at once', async () => {
    const many = Array.from({ length: 60 }, (_, i) => info(`Clip${i}`));
    const { platform, session } = setup(many);
    platform.confirmAnswer = false;
    renderWith(Animations, session, { speciesKey: 'carcharodontosaurus', displayName: 'Carcharodontosaurus' });

    await fireEvent.click(await screen.findByRole('button', { name: 'Select all' }));

    expect(platform.confirms[0]).toMatch(/60 animations/);
    expect((screen.getByLabelText('Pick Clip0') as HTMLInputElement).checked).toBe(false);
  });
});
