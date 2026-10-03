import { fireEvent, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { memoryStore } from '$lib/storage';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { Session } from '$lib/stores/session.svelte';
import JobBar from './JobBar.svelte';

describe('JobBar', () => {
  it('shows the running job and cancels it', async () => {
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    const cancel = vi.fn(async () => {});
    session.job = { id: 'j1', title: 'Data dump', fraction: 0.4, message: 'Waiting for the game (12 s)', cancel };

    renderWith(JobBar, session);

    expect(screen.getByText('Data dump')).toBeInTheDocument();
    expect(screen.getByText('Waiting for the game (12 s)')).toBeInTheDocument();
    expect((screen.getByRole('progressbar', { name: 'Data dump progress' }) as HTMLProgressElement).value).toBeCloseTo(0.4);
    await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(cancel).toHaveBeenCalled();
  });

  it('is empty when nothing runs', () => {
    const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
    renderWith(JobBar, session);
    expect(screen.queryByRole('progressbar')).toBeNull();
  });
});
