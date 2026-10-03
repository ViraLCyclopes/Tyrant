import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import Page from './+page.svelte';

describe('home page', () => {
  it('renders the app name', () => {
    render(Page);
    expect(screen.getByRole('heading', { name: 'Tyrant' })).toBeInTheDocument();
  });
});
