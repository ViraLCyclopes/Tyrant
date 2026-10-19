import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import BringInLooksDialog from './BringInLooksDialog.svelte';

const blue = { folder: 'materials/skins/blue', gameChanged: true, projectChanged: false };
const both = { folder: 'materials/species/Carcharodontosaurus', gameChanged: true, projectChanged: true };

describe('BringInLooksDialog', () => {
  it('both-changed rows offer game or project', async () => {
    const onContinue = vi.fn();
    render(BringInLooksDialog, {
      action: "Installing 'Red spot' again",
      mods: [{ id: 'red-spot', name: 'Red spot', changes: [blue, both], gameOnly: false }],
      onContinue,
      onCancel: () => {},
    });

    expect(screen.getByRole('dialog', { name: 'Bring in looks made in the game' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /Bring in.*blue/ })).toBeChecked();
    expect(screen.queryByRole('checkbox', { name: /Carcharodontosaurus/ })).toBeNull(); // a choice of side instead
    const game = screen.getByRole('radio', { name: /Game's.*Carcharodontosaurus/ });
    const project = screen.getByRole('radio', { name: /Project's.*Carcharodontosaurus/ });
    expect(game).toBeChecked();
    expect(project).not.toBeChecked();

    await fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(onContinue).toHaveBeenLastCalledWith({ bringIn: { 'red-spot': ['materials/skins/blue', 'materials/species/Carcharodontosaurus'] }, copy: [] });

    await fireEvent.click(project);
    await fireEvent.click(screen.getByRole('checkbox', { name: /Bring in.*blue/ }));
    await fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(onContinue).toHaveBeenLastCalledWith({ bringIn: { 'red-spot': [] }, copy: [] });
  });

  it('offers to copy a mod only in the game to the workspace first; Escape cancels', async () => {
    const onContinue = vi.fn();
    const onCancel = vi.fn();
    render(BringInLooksDialog, {
      action: 'Uninstalling Tyrant from the game',
      mods: [{ id: 'vanilla-looks', name: 'Vanilla Looks', changes: [], gameOnly: true }],
      onContinue,
      onCancel,
    });

    expect(screen.getByRole('checkbox', { name: /Copy Vanilla Looks to the workspace first/ })).toBeChecked();
    await fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(onContinue).toHaveBeenLastCalledWith({ bringIn: {}, copy: ['vanilla-looks'] });

    await fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalled();
  });
});
