import { render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import RemoveSkinDialog from './RemoveSkinDialog.svelte';

describe('RemoveSkinDialog', () => {
  it('a skin keeps its number', () => {
    render(RemoveSkinDialog, { name: 'Blue', onConfirm: vi.fn(), onCancel: vi.fn() });
    expect(screen.getByText(/Its number in the game stays reserved/)).toBeInTheDocument();
  });

  it("a starting skin says the species goes back to its template's game skin", () => {
    render(RemoveSkinDialog, { name: 'Mine', startingOf: 'tyrant.red-spot.anax', onConfirm: vi.fn(), onCancel: vi.fn() });
    expect(screen.getByText("tyrant.red-spot.anax will start with its template's game skin instead.")).toBeInTheDocument();
    expect(screen.queryByText(/Its number in the game stays reserved/)).toBeNull();
  });
});
