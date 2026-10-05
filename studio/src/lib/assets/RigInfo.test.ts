import { screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import RigInfo from './RigInfo.svelte';

describe('RigInfo', () => {
  it('lists the bones the animations and the growth move', async () => {
    const rpc = new FakeRpc().on('species.rigInfo', () => ({
      species: 'Allosaurus Anax', bones: 95, clipMoved: ['Pelvis', 'Femur.L'], growthMoved: ['Neck.002'], growthScaled: [],
      growthSupported: false, failures: [],
    }));
    const session = new Session(rpc, new FakePlatform(), memoryStore());
    session.workspace = workspaceStatus({ hasAssetIndex: true });
    renderWith(RigInfo, session, { speciesKey: 'allosaurusanax', displayName: 'Allosaurus Anax' });

    expect(await screen.findByText('Pelvis, Femur.L', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('Neck.002', { exact: false })).toBeInTheDocument();
    expect(screen.getByText(/not supported yet/)).toBeInTheDocument();
    expect(rpc.callsTo('species.rigInfo')[0]?.params).toEqual({ species: 'allosaurusanax' });
  });
});
