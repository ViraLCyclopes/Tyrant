import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import RemoveSpeciesDialog from './RemoveSpeciesDialog.svelte';
import type { SpeciesRemoval } from '$lib/rpc/types.gen';

const plan = (over: Partial<SpeciesRemoval> = {}): SpeciesRemoval => ({
  key: 'tyrant.red-spot.anax', name: 'Anax', skins: ['Alt 1', 'Blue'], models: 1, sounds: 2, prefabSettings: true, usedBy: [], ...over,
});

describe('RemoveSpeciesDialog', () => {
  it('lists what goes and warns about saved parks', () => {
    render(RemoveSpeciesDialog, { plan: plan(), onConfirm: vi.fn(), onCancel: vi.fn() });
    expect(screen.getByText("Delete species 'Anax'?")).toBeInTheDocument();
    expect(screen.getByText(/2 skins \(Alt 1, Blue\), 1 model, 2 sounds and its prefab settings/)).toBeInTheDocument();
    expect(screen.getByText(/Saved parks with Anax can no longer load its animals/)).toBeInTheDocument();
  });

  it('names other mods that use the species', () => {
    render(RemoveSpeciesDialog, { plan: plan({ usedBy: [{ modId: 'fans', name: 'Fan skins', skins: 3, models: 0, sounds: 0 }] }), onConfirm: vi.fn(), onCancel: vi.fn() });
    expect(screen.getByText(/Fan skins \(fans\): 3 skins/)).toBeInTheDocument();
  });

  it('deletes the files unless unticked', async () => {
    const onConfirm = vi.fn();
    render(RemoveSpeciesDialog, { plan: plan(), onConfirm, onCancel: vi.fn() });
    await fireEvent.click(screen.getByRole('button', { name: 'Delete species' }));
    expect(onConfirm).toHaveBeenLastCalledWith(true);
    await fireEvent.click(screen.getByRole('checkbox'));
    await fireEvent.click(screen.getByRole('button', { name: 'Delete species' }));
    expect(onConfirm).toHaveBeenLastCalledWith(false);
  });
});
