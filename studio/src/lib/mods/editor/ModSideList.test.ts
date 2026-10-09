import { fireEvent, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ModDetail, ModSkinDto } from '$lib/rpc/types.gen';
import { memoryStore } from '$lib/storage';
import { Session } from '$lib/stores/session.svelte';
import { FakePlatform } from '$lib/test/fakePlatform';
import { FakeRpc } from '$lib/test/fakeRpc';
import { renderWith } from '$lib/test/fixtures';
import { modDetail } from '$lib/test/modFixtures';
import ModSideList from './ModSideList.svelte';

const base = modDetail().skins[0];
const skin = (id: string, name: string, group: string | null = null): ModSkinDto => ({ ...base, id, key: `red-spot/${id}`, name, group });

function setup(detail: ModDetail = modDetail({ skins: [skin('a', 'Amber'), skin('c', 'Crimson', 'Jurassic Park'), skin('d', 'Dusk', 'Jurassic Park')] })) {
  const session = new Session(new FakeRpc(), new FakePlatform(), memoryStore());
  const props = { detail, check: null, selection: { kind: 'details' as const }, onSelect: vi.fn(), onAddSkin: vi.fn(), onArrange: vi.fn(), onRemoveSkin: vi.fn() };
  renderWith(ModSideList, session, props);
  return { session, props };
}

describe('ModSideList skins', () => {
  it('shows ungrouped skins, then each group under its heading with its species', () => {
    setup();
    expect(screen.getByRole('button', { name: 'Amber' })).toHaveTextContent('Carcharodontosaurus');
    expect(screen.getByRole('button', { name: 'Jurassic Park (2)' })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('button', { name: 'Crimson' })).toBeInTheDocument();
  });

  it('a group folds away and stays folded for this mod', async () => {
    const { session } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Jurassic Park (2)' }));
    expect(screen.queryByRole('button', { name: 'Crimson' })).toBeNull();
    expect(session.store.get('tyrant.mods.collapsedGroups.red-spot')).toBe('["Jurassic Park"]');
  });

  it("a skin's menu moves it and puts it in a group", async () => {
    const { props } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Dusk' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Move up' }));
    expect(props.onArrange).toHaveBeenLastCalledWith([{ id: 'a', group: null }, { id: 'd', group: 'Jurassic Park' }, { id: 'c', group: 'Jurassic Park' }]);

    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Amber' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Move to Jurassic Park' }));
    expect(props.onArrange).toHaveBeenLastCalledWith([{ id: 'c', group: 'Jurassic Park' }, { id: 'd', group: 'Jurassic Park' }, { id: 'a', group: 'Jurassic Park' }]);
  });

  it('New group… asks for a name', async () => {
    const { props } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Amber' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Move to a new group…' }));
    const dialog = screen.getByRole('dialog', { name: 'New group' });
    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Group name' }), { target: { value: 'Isla Sorna' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Move' }));
    expect(props.onArrange).toHaveBeenLastCalledWith([
      { id: 'c', group: 'Jurassic Park' }, { id: 'd', group: 'Jurassic Park' }, { id: 'a', group: 'Isla Sorna' },
    ]);
  });

  it('Alt+Down moves the skin; Remove skin… asks the editor', async () => {
    const { props } = setup();
    await fireEvent.keyDown(screen.getByRole('button', { name: 'Crimson' }), { key: 'ArrowDown', altKey: true });
    expect(props.onArrange).toHaveBeenLastCalledWith([{ id: 'a', group: null }, { id: 'd', group: 'Jurassic Park' }, { id: 'c', group: 'Jurassic Park' }]);

    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Crimson' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Remove skin…' }));
    expect(props.onRemoveSkin).toHaveBeenCalledWith('c');
  });

  it("a group's menu renames and ungroups; + on a group adds into it", async () => {
    const { props } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Actions for group Jurassic Park' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Ungroup' }));
    expect(props.onArrange).toHaveBeenLastCalledWith([{ id: 'a', group: null }, { id: 'c', group: null }, { id: 'd', group: null }]);

    await fireEvent.click(screen.getByRole('button', { name: 'Add a skin to Jurassic Park' }));
    expect(props.onAddSkin).toHaveBeenCalledWith('Jurassic Park');
    await fireEvent.click(screen.getByRole('button', { name: 'Add a skin' }));
    expect(props.onAddSkin).toHaveBeenLastCalledWith(null);
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
  });
});
