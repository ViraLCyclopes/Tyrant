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

/** The order the last arrange request builds from the current skins (the editor runs the builder on its latest detail). */
const lastOrder = (props: { detail: ModDetail; onArrange: ReturnType<typeof vi.fn> }) => props.onArrange.mock.lastCall![0](props.detail.skins);

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
    expect(lastOrder(props)).toEqual([{ id: 'a', group: null }, { id: 'd', group: 'Jurassic Park' }, { id: 'c', group: 'Jurassic Park' }]);

    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Amber' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Move to Jurassic Park' }));
    expect(lastOrder(props)).toEqual([{ id: 'c', group: 'Jurassic Park' }, { id: 'd', group: 'Jurassic Park' }, { id: 'a', group: 'Jurassic Park' }]);
  });

  it('New group… asks for a name', async () => {
    const { props } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Amber' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Move to a new group…' }));
    const dialog = screen.getByRole('dialog', { name: 'New group' });
    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Group name' }), { target: { value: 'Isla Sorna' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Move' }));
    expect(lastOrder(props)).toEqual([
      { id: 'c', group: 'Jurassic Park' }, { id: 'd', group: 'Jurassic Park' }, { id: 'a', group: 'Isla Sorna' },
    ]);
  });

  it('Alt+Down moves the skin; Remove skin… asks the editor', async () => {
    const { props } = setup();
    await fireEvent.keyDown(screen.getByRole('button', { name: 'Crimson' }), { key: 'ArrowDown', altKey: true });
    expect(lastOrder(props)).toEqual([{ id: 'a', group: null }, { id: 'd', group: 'Jurassic Park' }, { id: 'c', group: 'Jurassic Park' }]);

    await fireEvent.click(screen.getByRole('button', { name: 'Actions for Crimson' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Remove skin…' }));
    expect(props.onRemoveSkin).toHaveBeenCalledWith('c');
  });

  describe('dragging', () => {
    /** Presses on a skin, moves past the threshold, and lets go over `over` (clientY 10 is its lower half: jsdom rects are 0 high). */
    async function drag(from: HTMLElement, over: Element | null, { y = 10, cancel = false } = {}) {
      const original = document.elementFromPoint;
      document.elementFromPoint = () => over;
      try {
        await fireEvent.pointerDown(from, { button: 0, clientX: 5, clientY: 5, pointerId: 1 });
        await fireEvent.pointerMove(window, { clientX: 5, clientY: 30, pointerId: 1 });
        await fireEvent.pointerMove(window, { clientX: 5, clientY: y, pointerId: 1 });
        if (cancel) await fireEvent.keyDown(window, { key: 'Escape' });
        await fireEvent.pointerUp(window, { clientX: 5, clientY: y, pointerId: 1 });
        await fireEvent.click(from); // the browser's click after the press; a drag must not select
      } finally {
        document.elementFromPoint = original;
      }
    }

    it('dropping a skin on the lower half of another puts it after that skin, in its group', async () => {
      const { props } = setup();
      await drag(screen.getByRole('button', { name: 'Amber' }), screen.getByRole('button', { name: 'Dusk' }));
      expect(lastOrder(props)).toEqual([{ id: 'c', group: 'Jurassic Park' }, { id: 'd', group: 'Jurassic Park' }, { id: 'a', group: 'Jurassic Park' }]);
      expect(props.onSelect).not.toHaveBeenCalled();
    });

    it('dropping on the upper half puts it before that skin', async () => {
      const { props } = setup();
      await drag(screen.getByRole('button', { name: 'Dusk' }), screen.getByRole('button', { name: 'Crimson' }), { y: -10 });
      expect(lastOrder(props)).toEqual([{ id: 'a', group: null }, { id: 'd', group: 'Jurassic Park' }, { id: 'c', group: 'Jurassic Park' }]);
    });

    it('dropping on the Skins heading takes it out of its group; on a folded group heading puts it at that group end', async () => {
      const { props } = setup();
      await drag(screen.getByRole('button', { name: 'Dusk' }), screen.getByText(/^Skins \(/));
      expect(lastOrder(props)).toEqual([{ id: 'a', group: null }, { id: 'd', group: null }, { id: 'c', group: 'Jurassic Park' }]);

      await fireEvent.click(screen.getByRole('button', { name: 'Jurassic Park (2)' })); // fold it
      await drag(screen.getByRole('button', { name: 'Amber' }), screen.getByRole('button', { name: 'Jurassic Park (2)' }));
      expect(lastOrder(props)).toEqual([{ id: 'c', group: 'Jurassic Park' }, { id: 'd', group: 'Jurassic Park' }, { id: 'a', group: 'Jurassic Park' }]);
    });

    it('a press without moving still selects; Escape or a drop outside the list cancels', async () => {
      const { props } = setup();
      await fireEvent.pointerDown(screen.getByRole('button', { name: 'Amber' }), { button: 0, clientX: 5, clientY: 5, pointerId: 1 });
      await fireEvent.pointerUp(window, { clientX: 6, clientY: 6, pointerId: 1 });
      await fireEvent.click(screen.getByRole('button', { name: 'Amber' }));
      expect(props.onSelect).toHaveBeenCalledWith({ kind: 'skin', id: 'a' });

      await drag(screen.getByRole('button', { name: 'Amber' }), screen.getByRole('button', { name: 'Dusk' }), { cancel: true });
      await drag(screen.getByRole('button', { name: 'Amber' }), document.body);
      expect(props.onArrange).not.toHaveBeenCalled();
      expect(props.onSelect).toHaveBeenCalledTimes(1);
    });
  });

  it("a group's menu renames and ungroups; + on a group adds into it", async () => {
    const { props } = setup();
    await fireEvent.click(screen.getByRole('button', { name: 'Actions for group Jurassic Park' }));
    await fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Ungroup' }));
    expect(lastOrder(props)).toEqual([{ id: 'a', group: null }, { id: 'c', group: null }, { id: 'd', group: null }]);

    await fireEvent.click(screen.getByRole('button', { name: 'Add a skin to Jurassic Park' }));
    expect(props.onAddSkin).toHaveBeenCalledWith('Jurassic Park');
    await fireEvent.click(screen.getByRole('button', { name: 'Add a skin' }));
    expect(props.onAddSkin).toHaveBeenLastCalledWith(null);
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
  });
});
