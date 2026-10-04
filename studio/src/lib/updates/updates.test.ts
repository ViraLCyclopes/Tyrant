import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { compareVersions, Updates } from './updates.svelte';

const HOUR = 60 * 60 * 1000;

function setup(nexus: { version: string | null; url: string | null; error: string | null } = { version: null, url: null, error: null }) {
  const rpc = new FakeRpc().on('app.checkNexus', () => nexus);
  const platform = new FakePlatform();
  const session = new Session(rpc, platform, memoryStore());
  const store = memoryStore();
  let now = 1_000_000_000_000;
  let enabled = true;
  const updates = new Updates(session, store, () => enabled, () => now);
  return {
    rpc,
    platform,
    session,
    store,
    updates,
    advance: (ms: number) => (now += ms),
    disable: () => (enabled = false),
  };
}

describe('Updates', () => {
  it('offers a newer GitHub release, with the Nexus page when it has the same version', async () => {
    const { platform, updates } = setup({ version: '0.2.0', url: 'https://www.nexusmods.com/prehistorickingdom/mods/7', error: null });
    platform.update = { version: '0.2.0', notes: 'Fences!' };

    await updates.check();

    expect(updates.offer).toEqual({ kind: 'github', version: '0.2.0', notes: 'Fences!', nexusUrl: 'https://www.nexusmods.com/prehistorickingdom/mods/7' });
  });

  it('a newer version only on Nexus is shown with its page, not installed', async () => {
    const { updates } = setup({ version: '0.2.0', url: 'https://www.nexusmods.com/prehistorickingdom/mods/7', error: null });

    await updates.check();

    expect(updates.offer).toEqual({ kind: 'nexus', version: '0.2.0', url: 'https://www.nexusmods.com/prehistorickingdom/mods/7' });
  });

  it('when nothing is newer the manual check says so', async () => {
    const { updates } = setup({ version: '0.1.0', url: 'https://www.nexusmods.com/prehistorickingdom/mods/7', error: null });

    await updates.check();

    expect(updates.offer).toBeNull();
    expect(updates.note).toBe("You're up to date (Tyrant 0.1.0).");
  });

  it('a build without the updater key says updates are not set up', async () => {
    const { platform, updates } = setup();
    platform.update = 'unavailable';

    await updates.check();

    expect(updates.note).toContain("aren't set up in this build");
    expect(updates.note).toContain('github.com/ViraLCyclopes/Tyrant/releases');
  });

  it('the startup check runs at most once a day and not at all when turned off', async () => {
    const { platform, updates, advance } = setup();

    await updates.startup();
    await updates.startup();
    advance(25 * HOUR);
    await updates.startup();

    expect(platform.checks).toBe(2);
  });

  it('the startup check is skipped when the preference is off', async () => {
    const { platform, updates, disable } = setup();
    disable();

    await updates.startup();

    expect(platform.checks).toBe(0);
  });

  it('a failed startup check stays silent and is not retried right away; the manual check says what failed', async () => {
    const { platform, updates } = setup();
    platform.checkError = new Error('offline');

    await updates.startup();
    await updates.startup();
    expect(updates.note).toBeNull();
    expect(updates.offer).toBeNull();
    expect(platform.checks).toBe(1);

    await updates.check();
    expect(updates.note).toContain('offline');
  });

  it('Update now waits for a running job', async () => {
    const { platform, session, updates } = setup();
    platform.update = { version: '0.2.0', notes: '' };
    await updates.check();
    session.job = { id: 'j1', title: 'Install red-spot', fraction: 0.5, message: 'Copying', cancel: null };

    await updates.install();

    expect(platform.installed).toBe(0);
    expect(updates.note).toBe('Wait for Install red-spot to finish, then update.');
    session.job = null;
    await updates.install();
    expect(platform.installed).toBe(1);
  });

  it('while an update installs Tyrant counts as busy, so no job can start', async () => {
    const { platform, session, updates } = setup();
    platform.update = { version: '0.2.0', notes: '' };
    await updates.check();
    let finish = () => {};
    platform.installGate = new Promise<void>((resolve) => (finish = resolve));

    const installing = updates.install();
    await Promise.resolve();

    expect(updates.installing).toBe(true);
    expect(session.busy).toBe(true);
    expect(await session.runJob('assets.index', undefined, 'Asset index')).toBeNull();
    finish();
    await installing;
  });

  it('an unknown download size keeps the update installing', async () => {
    const { platform, updates } = setup();
    platform.update = { version: '0.2.0', notes: '' };
    await updates.check();
    platform.progressReports = [null];
    let finish = () => {};
    platform.installGate = new Promise<void>((resolve) => (finish = resolve));

    const installing = updates.install();
    await Promise.resolve();

    expect(updates.progress).toBeNull();
    expect(updates.installing).toBe(true);
    finish();
    await installing;
  });

  it('versions compare as releases write them', () => {
    expect(compareVersions('0.2.0', '0.1.9')).toBeGreaterThan(0);
    expect(compareVersions('v0.2.0', '0.2.0')).toBe(0);
    expect(compareVersions('0.2.0', '0.2.0-beta')).toBeGreaterThan(0);
    expect(compareVersions('0.10.0', '0.9.0')).toBeGreaterThan(0);
    expect(compareVersions('nonsense', '0.0.1')).toBeLessThan(0);
  });
});
