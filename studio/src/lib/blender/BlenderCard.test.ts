import { fireEvent, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith, workspaceStatus } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import type { BlenderStatusDto } from '$lib/rpc/types.gen';
import BlenderCard from './BlenderCard.svelte';

const found: BlenderStatusDto = {
  found: true, exe: 'C:\\B\\blender.exe', version: '5.2.2', supported: true, missingConfigured: null,
  addon: 'missing', addonInstalled: null, addonBundled: '0.1.0', problem: null,
};

function setup(status: BlenderStatusDto = found) {
  const rpc = new FakeRpc();
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  session.workspace = workspaceStatus({});
  rpc.on('blender.status', () => status);
  return { rpc, platform, session };
}

describe('BlenderCard', () => {
  it('shows the Blender found and installs the add-on', async () => {
    const { rpc, session } = setup();
    rpc.on('blender.installAddon', () => ({ ...found, addon: 'current', addonInstalled: '0.1.0' }));
    renderWith(BlenderCard, session);

    expect(await screen.findByText(/Blender 5\.2\.2/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Install add-on' }));

    await waitFor(() => expect(screen.getByText(/Add-on 0\.1\.0 installed/)).toBeInTheDocument());
  });

  it('offers Update add-on when the add-on is older', async () => {
    const { session } = setup({ ...found, addon: 'older', addonInstalled: '0.0.9' });
    renderWith(BlenderCard, session);
    expect(await screen.findByRole('button', { name: 'Update add-on' })).toBeInTheDocument();
  });

  it('offers Reinstall add-on when this Tyrant has the same version (a fixed build with the same number)', async () => {
    const { rpc, session } = setup({ ...found, addon: 'current', addonInstalled: '0.1.0' });
    rpc.on('blender.installAddon', () => ({ ...found, addon: 'current', addonInstalled: '0.1.0' }));
    renderWith(BlenderCard, session);

    await fireEvent.click(await screen.findByRole('button', { name: 'Reinstall add-on' }));

    await waitFor(() => expect(rpc.callsTo('blender.installAddon')).toHaveLength(1));
  });

  it('without Blender lets the user choose blender.exe', async () => {
    const { rpc, platform, session } = setup({ ...found, found: false, exe: null, version: null, supported: false, addon: 'unknown', problem: 'Blender was not found.' });
    rpc.on('blender.setPath', (p) => ({ ...found, exe: p.path ?? null }));
    platform.files.push('D:\\Blender\\blender.exe');
    renderWith(BlenderCard, session);

    expect(await screen.findByText('Blender was not found.')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Choose blender.exe…' }));

    await waitFor(() => expect(rpc.callsTo('blender.setPath')[0]?.params).toEqual({ path: 'D:\\Blender\\blender.exe' }));
  });

  it('says why Blender 4 cannot be used', async () => {
    const { session } = setup({ ...found, version: '4.5.0', supported: false, problem: "Tyrant's Blender tools need Blender 5.0 or newer (found 4.5)." });
    renderWith(BlenderCard, session);
    expect(await screen.findByText(/5\.0 or newer/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Install add-on' })).not.toBeInTheDocument();
  });
});
