import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import JsonTree from './JsonTree.svelte';

describe('JsonTree', () => {
  const value = {
    cost: 100,
    diet: 'Herbivore',
    stats: { hp: 5 },
    prefab: { $ref: { type: 'GameObject', name: 'Stego_Prefab', id: 3 } },
  };

  it('shows the first level and references by name', () => {
    render(JsonTree, { props: { value } });

    expect(screen.getByText('100')).toBeInTheDocument();
    expect(screen.getByText('"Herbivore"')).toBeInTheDocument();
    expect(screen.getByText(/→ Stego_Prefab/)).toBeInTheDocument();
    expect(screen.queryByText('hp:')).toBeNull();
  });

  it('expands nested objects on click', async () => {
    render(JsonTree, { props: { value } });

    await fireEvent.click(screen.getByRole('button', { name: /stats/ }));

    expect(screen.getByText('hp:')).toBeInTheDocument();
    expect(screen.getByText('5')).toBeInTheDocument();
  });
});
