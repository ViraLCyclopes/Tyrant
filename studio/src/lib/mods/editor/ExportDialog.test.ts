import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import ExportDialog from './ExportDialog.svelte';

describe('ExportDialog', () => {
  it('exports without the PNGs unless the box is ticked', async () => {
    const onexport = vi.fn();
    render(ExportDialog, { name: 'Red spot', onexport, oncancel: () => {} });
    const box = screen.getByRole('checkbox', { name: 'Include the original PNGs (bigger; lets others edit them without quality loss)' });
    expect(box).not.toBeChecked();

    await fireEvent.click(screen.getByRole('button', { name: 'Export…' }));
    expect(onexport).toHaveBeenLastCalledWith(false);
    await fireEvent.click(box);
    await fireEvent.click(screen.getByRole('button', { name: 'Export…' }));
    expect(onexport).toHaveBeenLastCalledWith(true);
  });

  it('Cancel and Escape cancel', async () => {
    const oncancel = vi.fn();
    render(ExportDialog, { name: 'Red spot', onexport: () => {}, oncancel });
    await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await fireEvent.keyDown(window, { key: 'Escape' });
    expect(oncancel).toHaveBeenCalledTimes(2);
  });
});
