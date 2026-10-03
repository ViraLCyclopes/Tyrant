import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { RpcError } from '$lib/rpc/client';
import ErrorBanner from './ErrorBanner.svelte';

describe('ErrorBanner', () => {
  it('shows the message, its details and a fix button', async () => {
    const onFix = vi.fn();
    const onDismiss = vi.fn();
    render(ErrorBanner, {
      props: { error: new RpcError('No dump arrived.\n  line one\n  line two', 'DUMP_TIMEOUT', 'INSTALL_DUMPER'), onFix, onDismiss },
    });

    expect(screen.getByRole('alert')).toHaveTextContent('No dump arrived.');
    expect(screen.getByText(/line two/)).toBeInTheDocument();
    expect(screen.getByText('DUMP_TIMEOUT')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Install dumper' }));
    await fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));

    expect(onFix).toHaveBeenCalledWith('INSTALL_DUMPER');
    expect(onDismiss).toHaveBeenCalled();
  });

  it('offers no fix button for a fix it does not know', () => {
    render(ErrorBanner, { props: { error: new RpcError('Odd.', 'X', 'SOMETHING_NEW'), onFix: vi.fn(), onDismiss: vi.fn() } });

    expect(screen.getAllByRole('button')).toHaveLength(1); // Dismiss only
  });
});
