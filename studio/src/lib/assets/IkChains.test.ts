import { screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import IkChains from './IkChains.svelte';

function setup(chains: { name: string; kind: string; joints: string[]; poleFrom: string | null; influence: number }[]) {
  const rpc = new FakeRpc();
  rpc.on('species.ik', () => ({ chains }));
  const session = new Session(rpc, new FakePlatform(), memoryStore());
  session.workspace = workspaceStatus({ hasAssetIndex: true });
  return { rpc, session };
}

describe('IkChains', () => {
  it('lists each chain with its bones and where its pole comes from', async () => {
    const { rpc, session } = setup([
      { name: 'Head', kind: 'head', joints: ['Spine.003', 'Neck', 'Head'], poleFrom: null, influence: 1 },
      { name: 'Leg L', kind: 'limb', joints: ['Femur.L', 'Calve.L', 'Foot.L', 'Heel.L'], poleFrom: 'Pelvis', influence: 1 },
    ]);
    renderWith(IkChains, session, { speciesKey: 'carcharodontosaurus', displayName: 'Carcharodontosaurus' });

    expect(await screen.findByText('Femur.L → Calve.L → Foot.L → Heel.L, pole from Pelvis', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('Spine.003 → Neck → Head', { exact: false })).toBeInTheDocument();
    expect(rpc.callsTo('species.ik')[0]?.params).toEqual({ key: 'carcharodontosaurus' });
  });

  it('says when a species has none', async () => {
    const { session } = setup([]);
    renderWith(IkChains, session, { speciesKey: 'titanoboa', displayName: 'Titanoboa' });
    expect(await screen.findByText('Titanoboa has no IK chains in the game.')).toBeInTheDocument();
  });
});
