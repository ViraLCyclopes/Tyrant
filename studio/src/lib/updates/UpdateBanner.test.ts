import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import UpdateBanner from './UpdateBanner.svelte';
import { Updates } from './updates.svelte';

function setup() {
  const platform = new FakePlatform();
  const session = new Session(new FakeRpc(), platform, memoryStore());
  const updates = new Updates(session, memoryStore(), () => true);
  return { platform, updates };
}

describe('UpdateBanner', () => {
  it('offers an update with its notes, Update now and Later', async () => {
    const { platform, updates } = setup();
    updates.offer = { version: '0.2.0', notes: 'Fences and sounds' };
    render(UpdateBanner, { props: { updates } });

    expect(screen.getByText('Tyrant 0.2.0 is available')).toBeInTheDocument();
    expect(screen.getByText('Fences and sounds')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Update now' }));
    expect(platform.installed).toBe(1);
    expect(screen.getByRole('button', { name: 'Later' })).toBeInTheDocument();
  });

  it('links to the release page for what changed', async () => {
    const { platform, updates } = setup();
    updates.offer = { version: '0.2.0', notes: '' };
    render(UpdateBanner, { props: { updates } });

    await fireEvent.click(screen.getByRole('button', { name: "What's new" }));

    expect(platform.opened).toEqual(['https://github.com/ViraLCyclopes/Tyrant/releases/tag/v0.2.0']);
  });

  it('while installing it shows progress, never a second Update now', () => {
    const { updates } = setup();
    updates.offer = { version: '0.2.0', notes: '' };
    updates.installing = true;
    updates.progress = null;
    render(UpdateBanner, { props: { updates } });

    expect(screen.queryByRole('button', { name: 'Update now' })).toBeNull();
    expect(screen.getByText(/Installing/)).toBeInTheDocument();
  });

  it('Later hides the offer', async () => {
    const { updates } = setup();
    updates.offer = { version: '0.2.0', notes: '' };
    render(UpdateBanner, { props: { updates } });

    await fireEvent.click(screen.getByRole('button', { name: 'Later' }));

    expect(updates.offer).toBeNull();
    expect(screen.queryByText('Tyrant 0.2.0 is available')).toBeNull();
  });

  it('shows a note from the manual check', () => {
    const { updates } = setup();
    updates.note = "You're up to date (Tyrant 0.1.0).";
    render(UpdateBanner, { props: { updates } });

    expect(screen.getByText("You're up to date (Tyrant 0.1.0).")).toBeInTheDocument();
  });
});
