import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { beforeEach, describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import type { BlenderStatusDto } from '$lib/rpc/types.gen';
import OpenInBlender from './OpenInBlender.svelte';

const ready: BlenderStatusDto = {
  found: true, exe: 'b.exe', version: '5.2.2', supported: true, missingConfigured: null,
  addon: 'current', addonInstalled: '0.1.0', addonBundled: '0.1.0', problem: null,
};

function setup(status: BlenderStatusDto = ready, gameChanged = false) {
  const rpc = new FakeRpc();
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  session.workspace = workspaceStatus({});
  rpc.on('blender.status', () => status);
  rpc.on('blender.open', () => ({ projectFile: 'p', how: 'running', gameChanged }));
  return { rpc, platform, session };
}

describe('OpenInBlender', () => {
  beforeEach(() => localStorage.clear());

  it('opens a game species with the chosen skin and options', async () => {
    const { rpc, session } = setup();
    renderWith(OpenInBlender, session, { species: 'Carcharodontosaurus', skins: ['Base', 'Alt 1'] });

    await fireEvent.change(await screen.findByLabelText('Skin'), { target: { value: 'Alt 1' } });
    await fireEvent.click(screen.getByLabelText('Include far LODs'));
    await fireEvent.click(screen.getByRole('button', { name: 'Open in Blender' }));

    await waitFor(() => expect(rpc.callsTo('blender.open')[0]?.params).toEqual({ species: 'Carcharodontosaurus', skin: 'Alt 1', mod: null, fresh: false, lods: true, sex: 'male', prefabRef: null, ik: true }));
  });

  it('opens a mod skin', async () => {
    const { rpc, session } = setup();
    renderWith(OpenInBlender, session, { species: 'Carcharodontosaurus', mod: 'reds', skin: 'red' });

    await fireEvent.click(await screen.findByLabelText('Start fresh'));
    await fireEvent.click(screen.getByRole('button', { name: 'Open in Blender' }));

    await waitFor(() => expect(rpc.callsTo('blender.open')[0]?.params).toEqual({ species: 'Carcharodontosaurus', skin: 'red', mod: 'reds', fresh: true, lods: false, sex: 'male', prefabRef: null, ik: true }));
  });

  it('Start fresh is used once, then unticked (a second open must not replace the .blend again)', async () => {
    const { rpc, session } = setup();
    renderWith(OpenInBlender, session, { species: 'Carcharodontosaurus', mod: 'reds', skin: 'red' });

    await fireEvent.click(await screen.findByLabelText('Start fresh'));
    await fireEvent.click(screen.getByRole('button', { name: 'Open in Blender' }));
    await waitFor(() => expect(rpc.callsTo('blender.open')).toHaveLength(1));
    await fireEvent.click(screen.getByRole('button', { name: 'Open in Blender' }));

    await waitFor(() => expect(rpc.callsTo('blender.open')).toHaveLength(2));
    expect(rpc.callsTo('blender.open')[1]?.params).toMatchObject({ fresh: false });
    expect(screen.getByLabelText('Start fresh')).not.toBeChecked();
  });

  it('can start as the female', async () => {
    const { rpc, session } = setup();
    renderWith(OpenInBlender, session, { species: 'Carcharodontosaurus', mod: 'reds', skin: 'red' });

    await fireEvent.change(await screen.findByLabelText('Sex'), { target: { value: 'female' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Open in Blender' }));

    await waitFor(() => expect(rpc.callsTo('blender.open')[0]?.params).toMatchObject({ sex: 'female' }));
  });

  it('is disabled with the reason when Blender is missing', async () => {
    const { session } = setup({ ...ready, found: false, supported: false, addon: 'unknown', problem: 'Blender was not found.' });
    renderWith(OpenInBlender, session, { species: 'Carcharodontosaurus', mod: 'm', skin: 's' });
    const button = await screen.findByRole('button', { name: 'Open in Blender' });
    await waitFor(() => expect(button).toBeDisabled());
    expect(button).toHaveAttribute('title', 'Blender was not found.');
  });

  it('offers to install a missing add-on before opening', async () => {
    const { rpc, platform, session } = setup({ ...ready, addon: 'missing', addonInstalled: null });
    rpc.on('blender.installAddon', () => ready);
    platform.confirmAnswer = true;
    renderWith(OpenInBlender, session, { species: 'Carcharodontosaurus', mod: 'm', skin: 's' });

    await waitFor(() => expect(screen.getByRole('button', { name: 'Open in Blender' })).toBeEnabled());
    await fireEvent.click(screen.getByRole('button', { name: 'Open in Blender' }));

    await waitFor(() => expect(rpc.callsTo('blender.installAddon')).toHaveLength(1));
    await waitFor(() => expect(rpc.callsTo('blender.open')).toHaveLength(1));
    expect(platform.confirms[0]).toMatch(/Install Tyrant's add-on/);
  });

  it('offers the update first when Blender has an older build of the add-on', async () => {
    const { rpc, platform, session } = setup({ ...ready, addon: 'changed' });
    rpc.on('blender.installAddon', () => ready);
    platform.confirmAnswer = true;
    renderWith(OpenInBlender, session, { species: 'Carcharodontosaurus', mod: 'm', skin: 's' });

    await waitFor(() => expect(screen.getByRole('button', { name: 'Open in Blender' })).toBeEnabled());
    await fireEvent.click(screen.getByRole('button', { name: 'Open in Blender' }));

    await waitFor(() => expect(rpc.callsTo('blender.installAddon')).toHaveLength(1));
    expect(platform.confirms[0]).toMatch(/Update Tyrant's add-on/);
  });

  it('does not open when the user declines the add-on install', async () => {
    const { rpc, platform, session } = setup({ ...ready, addon: 'missing', addonInstalled: null });
    platform.confirmAnswer = false;
    renderWith(OpenInBlender, session, { species: 'Carcharodontosaurus', mod: 'm', skin: 's' });

    await waitFor(() => expect(screen.getByRole('button', { name: 'Open in Blender' })).toBeEnabled());
    await fireEvent.click(screen.getByRole('button', { name: 'Open in Blender' }));

    await waitFor(() => expect(platform.confirms).toHaveLength(1));
    expect(rpc.callsTo('blender.open')).toHaveLength(0);
  });

  it('can open without IK controls, and remembers it', async () => {
    const { rpc, session } = setup();
    const first = renderWith(OpenInBlender, session, { species: 'Carcharodontosaurus', mod: 'reds', skin: 'red' });

    await fireEvent.click(await screen.findByLabelText('IK controls'));
    await fireEvent.click(screen.getByRole('button', { name: 'Open in Blender' }));
    await waitFor(() => expect(rpc.callsTo('blender.open')[0]?.params).toMatchObject({ ik: false }));
    first.unmount();

    renderWith(OpenInBlender, session, { species: 'Carcharodontosaurus', mod: 'reds', skin: 'red' });
    expect(await screen.findByLabelText('IK controls')).not.toBeChecked();
  });
});
